using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace LeonioIms
{
    public class ApiError : Exception
    {
        public int Status;
        public ApiError(int status, string message) : base(message) { Status = status; }
    }

    public class FileResult
    {
        public byte[] Bytes;
        public string ContentType;
        public string FileName;
    }

    public class Ctx
    {
        public HttpListenerRequest Req;
        public User User;
        public string Token;
        public Dictionary<string, object> Body;
        public string[] P;
        public NameValueCollection Q;

        public int Id { get { return int.Parse(P[0]); } }
        public bool Has(string k) { return Body != null && Body.ContainsKey(k); }
        public string S(string k)
        {
            if (Body == null || !Body.ContainsKey(k) || Body[k] == null) return null;
            var s = Convert.ToString(Body[k], CultureInfo.InvariantCulture).Trim();
            return s.Length == 0 ? null : s;
        }
        public int I(string k)
        {
            var s = S(k);
            int v;
            return s != null && int.TryParse(s, out v) ? v : 0;
        }
        public bool B(string k)
        {
            if (Body == null || !Body.ContainsKey(k) || Body[k] == null) return false;
            if (Body[k] is bool) return (bool)Body[k];
            return S(k) == "true";
        }
        public string Qs(string k)
        {
            var v = Q[k];
            return string.IsNullOrEmpty(v) ? null : v.Trim();
        }
    }

    public class Route
    {
        public string Method;
        public Regex Rx;
        public Func<Ctx, object> Handler;
        public bool Anonymous;
    }

    public class Session
    {
        public int UserId;
        public DateTime Expires;
    }

    public static class Store
    {
        public static readonly object Lock = new object();
        public static Db Db;
        public static string FilesDir;
        static string dbPath;
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public static void Init(string baseDir)
        {
            var dataDir = Path.Combine(baseDir, "data");
            FilesDir = Path.Combine(dataDir, "files");
            Directory.CreateDirectory(FilesDir);
            dbPath = Path.Combine(dataDir, "ims-db.json");
            if (File.Exists(dbPath))
            {
                Db = Json.Deserialize<Db>(File.ReadAllText(dbPath, Encoding.UTF8));
            }
            else
            {
                Db = new Db();
                Seed.Run();
                Save();
                Console.WriteLine("  Created a new database with demo data.");
            }
        }

        public static void Save()
        {
            var tmp = dbPath + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(Db), new UTF8Encoding(false));
            if (File.Exists(dbPath)) File.Replace(tmp, dbPath, dbPath + ".bak");
            else File.Move(tmp, dbPath);
        }

        public static int NextId() { return ++Db.Seq; }
    }

    public static class Security
    {
        public static string NewSalt()
        {
            var b = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            return Convert.ToBase64String(b);
        }

        public static string Hash(string password, string salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 20000))
                return Convert.ToBase64String(kdf.GetBytes(32));
        }

        public static void SetPassword(User u, string password)
        {
            u.Salt = NewSalt();
            u.PasswordHash = Hash(password, u.Salt);
        }

        public static bool Verify(User u, string password)
        {
            if (string.IsNullOrEmpty(u.Salt) || password == null) return false;
            var a = Hash(password, u.Salt);
            var b = u.PasswordHash ?? "";
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string NewToken()
        {
            var b = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }
    }

    public static class Program
    {
        static string webRoot;
        public static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();

        static readonly Dictionary<string, string> Mime = new Dictionary<string, string>
        {
            { ".html", "text/html; charset=utf-8" }, { ".js", "application/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" },
            { ".ico", "image/x-icon" }, { ".json", "application/json; charset=utf-8" }
        };

        public static void Main(string[] args)
        {
            int port = 8080;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--port") int.TryParse(args[i + 1], out port);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            webRoot = Path.Combine(baseDir, "wwwroot");
            Console.WriteLine();
            Console.WriteLine("  LEONIO GROUP IMS PORTAL");
            Console.WriteLine("  Integrated Management System");
            Console.WriteLine();
            Store.Init(baseDir);
            Api.Register();

            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:" + port + "/");
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            try { listener.Start(); }
            catch (HttpListenerException e)
            {
                Console.WriteLine("  Could not start on port " + port + ": " + e.Message);
                Console.WriteLine("  Another program may be using it. Try: LeonioIMS.exe --port 8090");
                return;
            }
            Console.WriteLine("  Running at http://localhost:" + port + "/");
            Console.WriteLine("  Keep this window open while using the portal. Press Ctrl+C to stop.");
            Console.WriteLine();
            if (args.Contains("--open"))
            {
                try { System.Diagnostics.Process.Start("http://localhost:" + port + "/"); }
                catch (Exception e) { Console.WriteLine("  Could not open the browser automatically (" + e.Message + "). Open the address above yourself."); }
            }

            while (true)
            {
                var ctx = listener.GetContext();
                ThreadPool.QueueUserWorkItem(o => Handle((HttpListenerContext)o), ctx);
            }
        }

        static void Handle(HttpListenerContext hc)
        {
            try
            {
                hc.Response.Headers["X-Content-Type-Options"] = "nosniff";
                hc.Response.Headers["X-Frame-Options"] = "DENY";
                var path = hc.Request.Url.AbsolutePath;
                if (path.StartsWith("/api/")) HandleApi(hc, path);
                else ServeStatic(hc, path);
            }
            catch (Exception e)
            {
                Console.WriteLine("  Error: " + e.Message);
                try { hc.Response.StatusCode = 500; } catch { }
            }
            finally
            {
                try { hc.Response.OutputStream.Close(); } catch { }
            }
        }

        static void ServeStatic(HttpListenerContext hc, string path)
        {
            if (path == "/" || path == "") path = "/index.html";
            var rel = Uri.UnescapeDataString(path).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(webRoot, rel));
            if (!full.StartsWith(Path.GetFullPath(webRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                hc.Response.StatusCode = 404;
                return;
            }
            string type;
            if (!Mime.TryGetValue(Path.GetExtension(full).ToLowerInvariant(), out type)) type = "application/octet-stream";
            var bytes = File.ReadAllBytes(full);
            hc.Response.ContentType = type;
            hc.Response.Headers["Cache-Control"] = "no-cache";
            hc.Response.ContentLength64 = bytes.Length;
            hc.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        static void HandleApi(HttpListenerContext hc, string path)
        {
            var req = hc.Request;
            hc.Response.Headers["Cache-Control"] = "no-store";
            try
            {
                var c = new Ctx { Req = req, Q = req.QueryString };
                if (req.HasEntityBody)
                {
                    string text;
                    using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                    if (text.Trim().Length > 0)
                        c.Body = Store.Json.Deserialize<Dictionary<string, object>>(text);
                }

                Route route = null;
                Match m = null;
                bool pathMatched = false;
                foreach (var rt in Api.Routes)
                {
                    var mm = rt.Rx.Match(path);
                    if (!mm.Success) continue;
                    pathMatched = true;
                    if (rt.Method != req.HttpMethod) continue;
                    route = rt; m = mm; break;
                }
                if (route == null) throw new ApiError(pathMatched ? 405 : 404, "Not found.");
                c.P = m.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray();

                object result;
                lock (Store.Lock)
                {
                    if (!route.Anonymous) Authenticate(c);
                    result = route.Handler(c);
                    if (req.HttpMethod != "GET") Store.Save();
                }

                var file = result as FileResult;
                if (file != null)
                {
                    hc.Response.ContentType = file.ContentType ?? "application/octet-stream";
                    var name = (file.FileName ?? "file").Replace("\"", "");
                    hc.Response.Headers["Content-Disposition"] = "inline; filename=\"" + name + "\"";
                    hc.Response.ContentLength64 = file.Bytes.Length;
                    hc.Response.OutputStream.Write(file.Bytes, 0, file.Bytes.Length);
                    return;
                }
                WriteJson(hc, 200, result);
            }
            catch (ApiError e)
            {
                WriteJson(hc, e.Status, new Dictionary<string, object> { { "error", e.Message } });
            }
            catch (Exception e)
            {
                Console.WriteLine("  API error on " + path + ": " + e);
                WriteJson(hc, 500, new Dictionary<string, object> { { "error", "Unexpected server error: " + e.Message } });
            }
        }

        static void Authenticate(Ctx c)
        {
            string token = null;
            var auth = c.Req.Headers["Authorization"];
            if (auth != null && auth.StartsWith("Bearer ")) token = auth.Substring(7).Trim();
            if (token == null) token = c.Q["t"];
            Session s;
            if (token == null || !Sessions.TryGetValue(token, out s) || s.Expires < DateTime.Now)
                throw new ApiError(401, "Your session has expired. Please sign in again.");
            var u = Store.Db.Users.FirstOrDefault(x => x.Id == s.UserId);
            if (u == null || !u.Active) throw new ApiError(401, "Your account is not active.");
            s.Expires = DateTime.Now.AddHours(8);
            c.User = u;
            c.Token = token;
        }

        static void WriteJson(HttpListenerContext hc, int status, object data)
        {
            var bytes = Encoding.UTF8.GetBytes(Store.Json.Serialize(data));
            hc.Response.StatusCode = status;
            hc.Response.ContentType = "application/json; charset=utf-8";
            hc.Response.ContentLength64 = bytes.Length;
            hc.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }
}
