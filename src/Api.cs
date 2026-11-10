using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LeonioIms
{
    public static partial class Api
    {
        public static readonly List<Route> Routes = new List<Route>();
        static readonly string[] FindingTypes = { "Major NC", "Minor NC", "Observation", "Opportunity for Improvement" };
        static readonly string[] Roles = { "Employee", "Contributor", "Controller", "Admin" };

        static Db D { get { return Store.Db; } }

        static void R(string method, string pattern, Func<Ctx, object> h)
        {
            Routes.Add(new Route { Method = method, Rx = new Regex("^" + pattern + "$"), Handler = h });
        }

        public static void Register()
        {
            Routes.Add(new Route { Method = "POST", Rx = new Regex("^/api/login$"), Handler = Login, Anonymous = true });
            R("POST", "/api/logout", c => { Program.Sessions.Remove(c.Token); return O("ok", true); });
            R("GET", "/api/me", Me);
            R("POST", "/api/me/password", ChangePassword);

            R("GET", "/api/documents", ListDocs);
            R("POST", "/api/documents", CreateDoc);
            R("GET", @"/api/documents/(\d+)", GetDoc);
            R("POST", @"/api/documents/(\d+)/revise", Revise);
            R("POST", @"/api/documents/(\d+)/obsolete", Obsolete);
            R("GET", @"/api/documents/(\d+)/acknowledgements", DocAcks);
            R("PUT", @"/api/revisions/(\d+)", UpdateRev);
            R("DELETE", @"/api/revisions/(\d+)", DeleteRev);
            R("POST", @"/api/revisions/(\d+)/submit", Submit);
            R("POST", @"/api/revisions/(\d+)/approve", c => Decide(c, true));
            R("POST", @"/api/revisions/(\d+)/reject", c => Decide(c, false));
            R("POST", @"/api/revisions/(\d+)/publish", Publish);
            R("POST", @"/api/revisions/(\d+)/acknowledge", Acknowledge);
            R("GET", @"/api/revisions/(\d+)/file", DownloadFile);

            R("GET", "/api/tasks", Tasks);

            R("GET", "/api/audits", ListAudits);
            R("POST", "/api/audits", CreateAudit);
            R("GET", @"/api/audits/(\d+)", GetAudit);
            R("PUT", @"/api/audits/(\d+)", UpdateAudit);
            R("POST", @"/api/audits/(\d+)/findings", AddFinding);

            R("GET", "/api/capas", ListCapas);
            R("POST", "/api/capas", CreateCapa);
            R("GET", @"/api/capas/(\d+)", GetCapa);
            R("PUT", @"/api/capas/(\d+)", UpdateCapa);
            R("POST", @"/api/capas/(\d+)/submit", SubmitCapa);
            R("POST", @"/api/capas/(\d+)/verify", VerifyCapa);

            R("GET", "/api/reports", Reports);
            R("GET", "/api/users", ListUsers);
            R("POST", "/api/users", CreateUser);
            R("PUT", @"/api/users/(\d+)", UpdateUser);
            R("POST", @"/api/users/(\d+)/reset-password", ResetPassword);
            R("GET", "/api/log", AuditTrail);
        }

        // ---------- helpers ----------

        public static Dictionary<string, object> O(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        public static int Level(string role)
        {
            switch (role)
            {
                case "Admin": return 4;
                case "Controller": return 3;
                case "Contributor": return 2;
                default: return 1;
            }
        }
        static int L(User u) { return Level(u.Role); }
        public static string Now() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
        public static string Today() { return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        static string DaysFromToday(int n) { return DateTime.Today.AddDays(n).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        static bool Overdue(string date) { return !string.IsNullOrEmpty(date) && string.CompareOrdinal(date, Today()) < 0; }

        static void Need(bool ok, string msg) { if (!ok) throw new ApiError(400, msg); }
        static void Allow(bool ok) { if (!ok) throw new ApiError(403, "You do not have permission to do this."); }
        static T Find<T>(IEnumerable<T> list, Func<T, bool> pred, string what) where T : class
        {
            var x = list.FirstOrDefault(pred);
            if (x == null) throw new ApiError(404, what + " not found.");
            return x;
        }
        static bool ValidDate(string s)
        {
            DateTime dt;
            return s != null && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        }

        static User UserById(int id) { return D.Users.FirstOrDefault(u => u.Id == id); }
        static string UName(int id) { var u = UserById(id); return u == null ? "" : u.Name; }
        static string CodeName(List<NamedCode> l, string code)
        {
            var x = l.FirstOrDefault(n => n.Code == code);
            return x == null ? code : x.Name;
        }

        public static void Log(User u, string action, string entity, int id, string detail)
        {
            D.Log.Add(new LogEntry
            {
                Id = Store.NextId(), At = Now(), UserId = u == null ? 0 : u.Id, UserName = u == null ? "System" : u.Name,
                Action = action, Entity = entity, EntityId = id, Detail = detail
            });
        }

        // A user is in a document's audience if the document applies to their business unit
        // (or is Group-wide) and, for Restricted documents, belongs to their department.
        public static bool Audience(User u, Doc d)
        {
            if (!(d.Bu == "GRP" || d.Bu == u.Bu)) return false;
            if (d.Classification == "Restricted") return u.Dept == d.Dept;
            return true;
        }
        static bool CanSee(User u, Doc d)
        {
            return L(u) >= 3 || d.OwnerId == u.Id || d.ApproverId == u.Id || Audience(u, d);
        }
        static bool CanManage(User u, Doc d)
        {
            return L(u) >= 3 || d.OwnerId == u.Id || d.ApproverId == u.Id ||
                   D.Revisions.Any(r => r.DocId == d.Id && r.CreatedBy == u.Id);
        }
        static bool CanEditRev(User u, Doc d, Revision r)
        {
            return r.CreatedBy == u.Id || d.OwnerId == u.Id || L(u) >= 3;
        }

        static Revision Eff(Doc d)
        {
            return d.EffectiveRevisionId == 0 ? null : D.Revisions.FirstOrDefault(r => r.Id == d.EffectiveRevisionId);
        }
        static readonly string[] WorkingStatuses = { "Draft", "InReview", "Approved", "Rejected" };
        static Revision Working(Doc d)
        {
            return D.Revisions.Where(r => r.DocId == d.Id && WorkingStatuses.Contains(r.Status))
                              .OrderByDescending(r => r.Id).FirstOrDefault();
        }
        static bool HasAcked(User u, Revision r) { return r != null && D.Acks.Any(a => a.RevisionId == r.Id && a.UserId == u.Id); }

        static string ReviewState(Doc d)
        {
            if (d.Status != "Effective" || string.IsNullOrEmpty(d.NextReviewDate)) return "";
            if (Overdue(d.NextReviewDate)) return "Overdue";
            if (string.CompareOrdinal(d.NextReviewDate, DaysFromToday(30)) <= 0) return "Due soon";
            return "";
        }

        static Dictionary<string, object> DocDto(Doc d, User u)
        {
            var eff = Eff(d);
            var w = CanManage(u, d) ? Working(d) : null;
            return O("id", d.Id, "docNo", d.DocNo, "title", d.Title,
                "type", d.Type, "typeName", CodeName(D.DocTypes, d.Type),
                "bu", d.Bu, "buName", CodeName(D.BusinessUnits, d.Bu),
                "dept", d.Dept, "deptName", CodeName(D.Departments, d.Dept),
                "ownerId", d.OwnerId, "ownerName", UName(d.OwnerId),
                "approverId", d.ApproverId, "approverName", UName(d.ApproverId),
                "standards", d.Standards, "classification", d.Classification, "process", d.Process,
                "reviewMonths", d.ReviewMonths, "nextReviewDate", d.NextReviewDate, "reviewState", ReviewState(d),
                "status", d.Status, "obsoleteReason", d.ObsoleteReason,
                "rev", eff == null ? null : eff.Rev, "effectiveDate", eff == null ? null : eff.EffectiveDate,
                "effectiveRevisionId", eff == null ? 0 : eff.Id,
                "acknowledged", eff != null && HasAcked(u, eff),
                "inAudience", Audience(u, d),
                "workingRev", w == null ? null : w.Rev, "workingStatus", w == null ? null : w.Status,
                "workingRevisionId", w == null ? 0 : w.Id);
        }

        static Dictionary<string, object> RevDto(Revision r, bool content)
        {
            return O("id", r.Id, "docId", r.DocId, "rev", r.Rev, "status", r.Status,
                "content", content ? r.Content : null, "changeSummary", r.ChangeSummary,
                "fileName", r.FileName, "hasFile", !string.IsNullOrEmpty(r.FileId),
                "createdBy", r.CreatedBy, "createdByName", UName(r.CreatedBy), "createdAt", r.CreatedAt,
                "submittedAt", r.SubmittedAt, "submittedByName", UName(r.SubmittedBy),
                "reviewedByName", UName(r.ReviewedBy), "reviewedAt", r.ReviewedAt, "reviewComment", r.ReviewComment,
                "publishedByName", UName(r.PublishedBy), "effectiveDate", r.EffectiveDate, "supersededAt", r.SupersededAt);
        }

        static Dictionary<string, object> UserDto(User u, bool full)
        {
            var d = O("id", u.Id, "name", u.Name, "role", u.Role, "bu", u.Bu, "buName", CodeName(D.BusinessUnits, u.Bu),
                "dept", u.Dept, "deptName", CodeName(D.Departments, u.Dept), "position", u.Position, "active", u.Active);
            if (full)
            {
                d["username"] = u.Username;
                d["email"] = u.Email;
                d["mustChangePassword"] = u.MustChangePassword;
                d["locked"] = !string.IsNullOrEmpty(u.LockedUntil) && string.CompareOrdinal(u.LockedUntil, Now()) > 0;
            }
            return d;
        }

        static void CheckPasswordStrength(string p)
        {
            Need(p != null && p.Length >= 8 && p.Any(char.IsLetter) && p.Any(char.IsDigit),
                "Password must be at least 8 characters and contain letters and numbers.");
        }

        // ---------- auth ----------

        static object Login(Ctx c)
        {
            var username = (c.S("username") ?? "").ToLowerInvariant();
            var u = D.Users.FirstOrDefault(x => (x.Username ?? "").ToLowerInvariant() == username);
            if (u != null && !string.IsNullOrEmpty(u.LockedUntil) && string.CompareOrdinal(u.LockedUntil, Now()) > 0)
                throw new ApiError(401, "This account is temporarily locked after repeated failed sign-ins. Try again after " + u.LockedUntil + ".");
            if (u == null || !u.Active || !Security.Verify(u, c.S("password")))
            {
                if (u != null)
                {
                    u.FailedLogins++;
                    if (u.FailedLogins >= 5)
                    {
                        u.LockedUntil = DateTime.Now.AddMinutes(15).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        u.FailedLogins = 0;
                        Log(u, "Account locked", "User", u.Id, "5 failed sign-in attempts");
                    }
                }
                Log(null, "Sign-in failed", "User", u == null ? 0 : u.Id, "Username: " + username);
                throw new ApiError(401, "Incorrect username or password.");
            }
            u.FailedLogins = 0;
            u.LockedUntil = null;
            var token = Security.NewToken();
            Program.Sessions[token] = new Session { UserId = u.Id, Expires = DateTime.Now.AddHours(8) };
            Log(u, "Signed in", "User", u.Id, "");
            return O("token", token);
        }

        static object Me(Ctx c)
        {
            return O("user", UserDto(c.User, true), "level", L(c.User),
                "settings", O("businessUnits", D.BusinessUnits, "departments", D.Departments, "docTypes", D.DocTypes,
                              "findingTypes", FindingTypes, "roles", Roles));
        }

        static object ChangePassword(Ctx c)
        {
            Need(Security.Verify(c.User, c.S("current")), "Your current password is incorrect.");
            var p = c.S("password");
            CheckPasswordStrength(p);
            Security.SetPassword(c.User, p);
            c.User.MustChangePassword = false;
            Log(c.User, "Changed password", "User", c.User.Id, "");
            return O("ok", true);
        }

        // ---------- documents ----------

        static object ListDocs(Ctx c)
        {
            var u = c.User;
            var scope = c.Qs("scope") ?? "library";
            IEnumerable<Doc> docs;
            if (scope == "manage")
            {
                Allow(L(u) >= 2);
                docs = D.Documents.Where(d => CanManage(u, d));
                var st = c.Qs("status");
                if (st != null) docs = docs.Where(d => d.Status == st || (st == "Pending" && Working(d) != null));
            }
            else
            {
                docs = D.Documents.Where(d => d.Status == "Effective" && CanSee(u, d));
                if (c.Qs("mine") == "1") docs = docs.Where(d => Audience(u, d));
            }

            var type = c.Qs("type"); if (type != null) docs = docs.Where(d => d.Type == type);
            var bu = c.Qs("bu"); if (bu != null) docs = docs.Where(d => d.Bu == bu);
            var dept = c.Qs("dept"); if (dept != null) docs = docs.Where(d => d.Dept == dept);
            var q = c.Qs("q");
            if (q != null)
            {
                var terms = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                docs = docs.Where(d =>
                {
                    var eff = Eff(d);
                    var hay = (d.DocNo + " " + d.Title + " " + d.Process + " " + d.Standards + " " +
                               CodeName(D.Departments, d.Dept) + " " + (eff == null ? "" : eff.Content)).ToLowerInvariant();
                    return terms.All(t => hay.Contains(t));
                });
            }

            var list = docs.ToList();
            if (c.Qs("sort") == "recent")
                list = list.OrderByDescending(d => { var e = Eff(d); return e == null ? "" : e.EffectiveDate; }).ThenByDescending(d => d.EffectiveRevisionId).ToList();
            else if (scope == "manage")
                list = list.OrderByDescending(d => D.Revisions.Where(r => r.DocId == d.Id).Select(r => r.Id).DefaultIfEmpty(0).Max()).ToList();
            else
                list = list.OrderBy(d => d.DocNo).ToList();

            int limit;
            if (int.TryParse(c.Qs("limit") ?? "", out limit) && limit > 0) list = list.Take(limit).ToList();
            return list.Select(d => DocDto(d, u)).ToList();
        }

        static object GetDoc(Ctx c)
        {
            var u = c.User;
            var d = Find(D.Documents, x => x.Id == c.Id, "Document");
            Allow(CanSee(u, d));
            bool manage = CanManage(u, d);
            if (!manage && d.Status != "Effective") throw new ApiError(404, "This document is not currently available.");
            var eff = Eff(d);
            var w = manage ? Working(d) : null;

            var revs = D.Revisions.Where(r => r.DocId == d.Id).OrderByDescending(r => r.Id)
                .Where(r => manage || r.Status == "Effective" || r.Status == "Superseded")
                .Select(r => RevDto(r, manage)).ToList();

            object ackStats = null;
            if (manage && eff != null)
            {
                var audience = D.Users.Where(x => x.Active && Audience(x, d)).ToList();
                var acked = audience.Count(x => HasAcked(x, eff));
                ackStats = O("required", audience.Count, "acknowledged", acked);
            }

            bool editable = w != null && (w.Status == "Draft" || w.Status == "Rejected") && CanEditRev(u, d, w);
            var perms = O(
                "manage", manage,
                "edit", editable,
                "submit", editable,
                "delete", editable,
                "approve", w != null && w.Status == "InReview" && d.ApproverId == u.Id && w.CreatedBy != u.Id,
                "publish", w != null && w.Status == "Approved" && L(u) >= 3,
                "revise", (d.OwnerId == u.Id || L(u) >= 3) && d.Status == "Effective" && w == null,
                "obsolete", L(u) >= 3 && d.Status == "Effective" && w == null,
                "acknowledge", eff != null && d.Status == "Effective" && !HasAcked(u, eff));

            var history = manage
                ? D.Log.Where(l => l.Entity == "Document" && l.EntityId == d.Id).OrderByDescending(l => l.Id).Take(100).ToList()
                : null;

            return O("doc", DocDto(d, u), "effective", eff == null || d.Status == "Draft" ? null : RevDto(eff, true),
                "working", w == null ? null : RevDto(w, true), "revisions", revs,
                "ackStats", ackStats, "perms", perms, "history", history,
                "myAck", eff == null ? null : D.Acks.Where(a => a.RevisionId == eff.Id && a.UserId == u.Id).Select(a => a.At).FirstOrDefault());
        }

        static void ApplyFile(Ctx c, Revision r)
        {
            if (c.B("removeFile")) { r.FileId = null; r.FileName = null; r.FileType = null; }
            var data = c.S("fileData");
            if (data == null) return;
            var comma = data.IndexOf(',');
            if (data.StartsWith("data:") && comma > 0) data = data.Substring(comma + 1);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(data); }
            catch { throw new ApiError(400, "The attached file could not be read."); }
            Need(bytes.Length <= 25 * 1024 * 1024, "Attachments are limited to 25 MB.");
            var fid = Guid.NewGuid().ToString("N");
            File.WriteAllBytes(Path.Combine(Store.FilesDir, fid), bytes);
            r.FileId = fid;
            r.FileName = Path.GetFileName(c.S("fileName") ?? "attachment");
            r.FileType = c.S("fileType") ?? "application/octet-stream";
        }

        static void ApplyMeta(Ctx c, Doc d, User u)
        {
            if (c.Has("title")) { Need(c.S("title") != null, "Title is required."); d.Title = c.S("title"); }
            if (c.Has("standards")) d.Standards = c.S("standards");
            if (c.Has("process")) d.Process = c.S("process");
            if (c.Has("classification")) d.Classification = c.S("classification") == "Restricted" ? "Restricted" : "Internal";
            if (c.Has("reviewMonths"))
            {
                var m = c.I("reviewMonths");
                Need(m >= 1 && m <= 60, "Review period must be between 1 and 60 months.");
                d.ReviewMonths = m;
            }
            if (c.Has("approverId"))
            {
                var a = UserById(c.I("approverId"));
                Need(a != null && a.Active && L(a) >= 2, "Select an approver with process owner, controller or admin rights.");
                d.ApproverId = a.Id;
            }
            if (c.Has("ownerId") && L(u) >= 3)
            {
                var o = UserById(c.I("ownerId"));
                Need(o != null && o.Active && L(o) >= 2, "The document owner must have process owner rights or higher.");
                d.OwnerId = o.Id;
            }
        }

        static object CreateDoc(Ctx c)
        {
            var u = c.User;
            Allow(L(u) >= 2);
            string type = c.S("type"), bu = c.S("bu"), dept = c.S("dept");
            Need(c.S("title") != null, "Title is required.");
            Need(D.DocTypes.Any(t => t.Code == type), "Select a document type.");
            Need(D.BusinessUnits.Any(t => t.Code == bu), "Select the company or business unit.");
            Need(D.Departments.Any(t => t.Code == dept), "Select the department or process area.");
            Need(c.Has("approverId"), "Select an approver.");

            var prefix = bu + "-" + dept + "-" + type + "-";
            var n = D.Documents.Count(x => x.DocNo.StartsWith(prefix)) + 1;
            var d = new Doc
            {
                Id = Store.NextId(), DocNo = prefix + n.ToString("000"), Type = type, Bu = bu, Dept = dept,
                OwnerId = u.Id, Status = "Draft", ReviewMonths = 24, Classification = "Internal", CreatedAt = Now()
            };
            ApplyMeta(c, d, u);
            D.Documents.Add(d);
            var r = new Revision
            {
                Id = Store.NextId(), DocId = d.Id, Rev = "00", Status = "Draft", Content = c.S("content") ?? "",
                ChangeSummary = c.S("changeSummary") ?? "Initial issue", CreatedBy = u.Id, CreatedAt = Now()
            };
            ApplyFile(c, r);
            D.Revisions.Add(r);
            Log(u, "Created document", "Document", d.Id, d.DocNo + " " + d.Title + " - Rev 00 draft");
            return O("id", d.Id, "revisionId", r.Id);
        }

        static Revision RevWithDoc(Ctx c, out Doc d)
        {
            var r = Find(D.Revisions, x => x.Id == c.Id, "Revision");
            d = Find(D.Documents, x => x.Id == r.DocId, "Document");
            return r;
        }

        static object UpdateRev(Ctx c)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            Allow(CanEditRev(c.User, d, r));
            Need(r.Status == "Draft" || r.Status == "Rejected", "Only draft or returned revisions can be edited.");
            if (c.Has("content")) r.Content = c.S("content") ?? "";
            if (c.Has("changeSummary")) r.ChangeSummary = c.S("changeSummary");
            ApplyMeta(c, d, c.User);
            ApplyFile(c, r);
            if (r.Status == "Rejected") r.Status = "Draft";
            Log(c.User, "Edited draft", "Document", d.Id, "Rev " + r.Rev);
            return O("ok", true);
        }

        static object DeleteRev(Ctx c)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            Allow(CanEditRev(c.User, d, r));
            Need(r.Status == "Draft" || r.Status == "Rejected", "Only unpublished drafts can be deleted.");
            D.Revisions.Remove(r);
            bool docDeleted = !D.Revisions.Any(x => x.DocId == d.Id);
            if (docDeleted) D.Documents.Remove(d);
            Log(c.User, docDeleted ? "Deleted draft document" : "Discarded draft revision", "Document", d.Id, d.DocNo + " Rev " + r.Rev);
            return O("docDeleted", docDeleted);
        }

        static object Submit(Ctx c)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            Allow(CanEditRev(c.User, d, r));
            Need(r.Status == "Draft" || r.Status == "Rejected", "This revision has already been submitted.");
            Need(!string.IsNullOrWhiteSpace(r.Content) || !string.IsNullOrEmpty(r.FileId), "Add the document content or attach a file before submitting.");
            Need(!string.IsNullOrWhiteSpace(r.ChangeSummary), "Describe what changed in this revision.");
            Need(d.ApproverId != r.CreatedBy && d.ApproverId != c.User.Id,
                "The approver cannot be the person who prepared or submitted this revision. Choose a different approver.");
            r.Status = "InReview";
            r.SubmittedBy = c.User.Id;
            r.SubmittedAt = Now();
            r.ReviewComment = null;
            Log(c.User, "Submitted for approval", "Document", d.Id, "Rev " + r.Rev + " sent to " + UName(d.ApproverId));
            return O("ok", true);
        }

        static object Decide(Ctx c, bool approve)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            Need(r.Status == "InReview", "This revision is not awaiting approval.");
            Allow(d.ApproverId == c.User.Id && r.CreatedBy != c.User.Id);
            var comment = c.S("comment");
            if (!approve) Need(comment != null, "Explain why the revision is being returned.");
            r.Status = approve ? "Approved" : "Rejected";
            r.ReviewedBy = c.User.Id;
            r.ReviewedAt = Now();
            r.ReviewComment = comment;
            Log(c.User, approve ? "Approved revision" : "Returned revision for correction", "Document", d.Id,
                "Rev " + r.Rev + (comment == null ? "" : ": " + comment));
            return O("ok", true);
        }

        static object Publish(Ctx c)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            Allow(L(c.User) >= 3);
            Need(r.Status == "Approved", "Only approved revisions can be published.");
            var prev = Eff(d);
            if (prev != null && prev.Status == "Effective")
            {
                prev.Status = "Superseded";
                prev.SupersededAt = Today();
            }
            r.Status = "Effective";
            r.PublishedBy = c.User.Id;
            r.EffectiveDate = Today();
            d.EffectiveRevisionId = r.Id;
            d.Status = "Effective";
            d.NextReviewDate = DateTime.Today.AddMonths(d.ReviewMonths).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var audience = D.Users.Count(x => x.Active && Audience(x, d));
            Log(c.User, "Published revision", "Document", d.Id,
                "Rev " + r.Rev + " effective " + r.EffectiveDate + (prev != null ? "; Rev " + prev.Rev + " superseded" : "") +
                "; " + audience + " employees asked to acknowledge");
            return O("ok", true);
        }

        static object Revise(Ctx c)
        {
            var d = Find(D.Documents, x => x.Id == c.Id, "Document");
            Allow(d.OwnerId == c.User.Id || L(c.User) >= 3);
            Need(d.Status == "Effective", "Only effective documents can be revised.");
            Need(Working(d) == null, "A revision of this document is already in progress.");
            var eff = Eff(d);
            var next = D.Revisions.Where(x => x.DocId == d.Id).Select(x => int.Parse(x.Rev)).DefaultIfEmpty(-1).Max() + 1;
            var r = new Revision
            {
                Id = Store.NextId(), DocId = d.Id, Rev = next.ToString("00"), Status = "Draft",
                Content = eff == null ? "" : eff.Content, ChangeSummary = "",
                FileId = eff == null ? null : eff.FileId, FileName = eff == null ? null : eff.FileName, FileType = eff == null ? null : eff.FileType,
                CreatedBy = c.User.Id, CreatedAt = Now()
            };
            D.Revisions.Add(r);
            Log(c.User, "Started revision", "Document", d.Id, "Rev " + r.Rev + " draft created");
            return O("revisionId", r.Id);
        }

        static object Obsolete(Ctx c)
        {
            var d = Find(D.Documents, x => x.Id == c.Id, "Document");
            Allow(L(c.User) >= 3);
            Need(d.Status == "Effective", "Only effective documents can be made obsolete.");
            Need(Working(d) == null, "Finish or discard the revision in progress first.");
            var reason = c.S("reason");
            Need(reason != null, "Give a reason for withdrawing the document.");
            d.Status = "Obsolete";
            d.ObsoleteReason = reason;
            var eff = Eff(d);
            if (eff != null) { eff.Status = "Withdrawn"; eff.SupersededAt = Today(); }
            Log(c.User, "Made obsolete", "Document", d.Id, reason);
            return O("ok", true);
        }

        static object Acknowledge(Ctx c)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            Need(r.Status == "Effective" && d.Status == "Effective", "Only the current effective revision can be acknowledged.");
            Allow(CanSee(c.User, d));
            if (!HasAcked(c.User, r))
            {
                D.Acks.Add(new Ack { Id = Store.NextId(), RevisionId = r.Id, UserId = c.User.Id, At = Now() });
                Log(c.User, "Acknowledged", "Document", d.Id, d.DocNo + " Rev " + r.Rev);
            }
            return O("ok", true);
        }

        static object DocAcks(Ctx c)
        {
            var d = Find(D.Documents, x => x.Id == c.Id, "Document");
            Allow(CanManage(c.User, d));
            var eff = Eff(d);
            if (eff == null) return new List<object>();
            return D.Users.Where(x => x.Active && Audience(x, d)).OrderBy(x => x.Name).Select(x => O(
                "userId", x.Id, "name", x.Name, "position", x.Position,
                "buName", CodeName(D.BusinessUnits, x.Bu), "deptName", CodeName(D.Departments, x.Dept),
                "ackedAt", D.Acks.Where(a => a.RevisionId == eff.Id && a.UserId == x.Id).Select(a => a.At).FirstOrDefault()
            )).ToList();
        }

        static object DownloadFile(Ctx c)
        {
            Doc d;
            var r = RevWithDoc(c, out d);
            if (r.Status == "Effective" && d.Status == "Effective") Allow(CanSee(c.User, d));
            else Allow(CanManage(c.User, d));
            Need(!string.IsNullOrEmpty(r.FileId), "This revision has no attachment.");
            var path = Path.Combine(Store.FilesDir, r.FileId);
            if (!File.Exists(path)) throw new ApiError(404, "The attachment file is missing from the server.");
            Log(c.User, "Opened attachment", "Document", d.Id, d.DocNo + " Rev " + r.Rev + " - " + r.FileName);
            return new FileResult { Bytes = File.ReadAllBytes(path), ContentType = r.FileType, FileName = r.FileName };
        }
    }
}
