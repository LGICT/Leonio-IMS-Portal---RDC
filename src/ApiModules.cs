using System;
using System.Collections.Generic;
using System.Linq;

namespace LeonioIms
{
    public static partial class Api
    {
        // ---------- tasks ----------

        static object Tasks(Ctx c)
        {
            var u = c.User;
            var pendingAcks = D.Documents.Where(d => d.Status == "Effective" && Audience(u, d) && !HasAcked(u, Eff(d)))
                .OrderByDescending(d => d.EffectiveRevisionId).Select(d => DocDto(d, u)).ToList();

            var toApprove = D.Revisions.Where(r => r.Status == "InReview").Select(r => new { r, d = D.Documents.First(x => x.Id == r.DocId) })
                .Where(x => x.d.ApproverId == u.Id && x.r.CreatedBy != u.Id)
                .Select(x => O("doc", DocDto(x.d, u), "revision", RevDto(x.r, false))).ToList();

            var toPublish = L(u) >= 3
                ? D.Revisions.Where(r => r.Status == "Approved").Select(r => O("doc", DocDto(D.Documents.First(x => x.Id == r.DocId), u), "revision", RevDto(r, false))).ToList()
                : new List<Dictionary<string, object>>();

            var myDrafts = D.Revisions.Where(r => r.Status == "Draft" || r.Status == "Rejected" || r.Status == "InReview" || r.Status == "Approved")
                .Select(r => new { r, d = D.Documents.First(x => x.Id == r.DocId) })
                .Where(x => x.r.CreatedBy == u.Id || x.d.OwnerId == u.Id)
                .Select(x => O("doc", DocDto(x.d, u), "revision", RevDto(x.r, false))).ToList();

            var reviewsDue = D.Documents.Where(d => d.OwnerId == u.Id && ReviewState(d) != "")
                .OrderBy(d => d.NextReviewDate).Select(d => DocDto(d, u)).ToList();

            var myCapas = D.Capas.Where(x => x.OwnerId == u.Id && (x.Status == "Open" || x.Status == "In Progress"))
                .OrderBy(x => x.DueDate).Select(CapaDto).ToList();

            var toVerify = D.Capas.Where(x => x.Status == "Pending Verification" && x.OwnerId != u.Id && (L(u) >= 3 || x.CreatedBy == u.Id))
                .Select(CapaDto).ToList();

            var myAudits = D.Audits.Where(a => a.LeadAuditorId == u.Id && a.Status != "Completed")
                .OrderBy(a => a.PlannedDate).Select(AuditDto).ToList();

            return O("pendingAcks", pendingAcks, "toApprove", toApprove, "toPublish", toPublish, "myDrafts", myDrafts,
                "reviewsDue", reviewsDue, "myCapas", myCapas, "toVerify", toVerify, "myAudits", myAudits,
                "total", pendingAcks.Count + toApprove.Count + toPublish.Count + reviewsDue.Count + myCapas.Count + toVerify.Count +
                         myDrafts.Count(x => { var s = (string)((Dictionary<string, object>)x["revision"])["status"]; return s == "Draft" || s == "Rejected"; }));
        }

        // ---------- internal audits ----------

        static Dictionary<string, object> AuditDto(Audit a)
        {
            var f = D.Findings.Where(x => x.AuditId == a.Id).ToList();
            return O("id", a.Id, "ref", a.Ref, "title", a.Title, "standard", a.Standard,
                "bu", a.Bu, "buName", CodeName(D.BusinessUnits, a.Bu), "dept", a.Dept, "deptName", CodeName(D.Departments, a.Dept),
                "scope", a.Scope, "leadAuditorId", a.LeadAuditorId, "leadAuditorName", UName(a.LeadAuditorId),
                "plannedDate", a.PlannedDate, "status", a.Status, "summary", a.Summary, "completedAt", a.CompletedAt,
                "findings", f.Count, "ncs", f.Count(x => x.Type.EndsWith("NC")));
        }

        static void ApplyAudit(Ctx c, Audit a)
        {
            if (c.Has("title")) { Need(c.S("title") != null, "Audit title is required."); a.Title = c.S("title"); }
            if (c.Has("standard")) a.Standard = c.S("standard");
            if (c.Has("bu")) { Need(D.BusinessUnits.Any(x => x.Code == c.S("bu")), "Select the business unit."); a.Bu = c.S("bu"); }
            if (c.Has("dept")) { Need(D.Departments.Any(x => x.Code == c.S("dept")), "Select the department."); a.Dept = c.S("dept"); }
            if (c.Has("scope")) a.Scope = c.S("scope");
            if (c.Has("summary")) a.Summary = c.S("summary");
            if (c.Has("plannedDate")) { Need(ValidDate(c.S("plannedDate")), "Enter a valid planned date."); a.PlannedDate = c.S("plannedDate"); }
            if (c.Has("leadAuditorId"))
            {
                var lead = UserById(c.I("leadAuditorId"));
                Need(lead != null && lead.Active && L(lead) >= 2, "Select a lead auditor with contributor rights or higher.");
                a.LeadAuditorId = lead.Id;
            }
        }

        static object ListAudits(Ctx c)
        {
            Allow(L(c.User) >= 2);
            IEnumerable<Audit> list = D.Audits;
            var st = c.Qs("status"); if (st != null) list = list.Where(a => a.Status == st);
            return list.OrderByDescending(a => a.PlannedDate).Select(AuditDto).ToList();
        }

        static object CreateAudit(Ctx c)
        {
            Allow(L(c.User) >= 2);
            var year = DateTime.Today.Year;
            var n = D.Audits.Count(a => a.Ref.StartsWith("IA-" + year + "-")) + 1;
            var a2 = new Audit { Id = Store.NextId(), Ref = "IA-" + year + "-" + n.ToString("000"), Status = "Planned", CreatedBy = c.User.Id, CreatedAt = Now(), LeadAuditorId = c.User.Id };
            Need(c.S("title") != null && c.S("bu") != null && c.S("dept") != null && c.S("plannedDate") != null,
                "Title, business unit, department and planned date are required.");
            ApplyAudit(c, a2);
            D.Audits.Add(a2);
            Log(c.User, "Planned audit", "Audit", a2.Id, a2.Ref + " " + a2.Title);
            return O("id", a2.Id);
        }

        static object GetAudit(Ctx c)
        {
            Allow(L(c.User) >= 2);
            var a = Find(D.Audits, x => x.Id == c.Id, "Audit");
            var findings = D.Findings.Where(f => f.AuditId == a.Id).OrderBy(f => f.Id).Select(f =>
            {
                var capa = D.Capas.FirstOrDefault(x => x.Id == f.CapaId);
                return O("id", f.Id, "ref", f.Ref, "type", f.Type, "clause", f.Clause, "description", f.Description,
                    "evidence", f.Evidence, "createdAt", f.CreatedAt, "createdByName", UName(f.CreatedBy),
                    "capaId", f.CapaId, "capaRef", capa == null ? null : capa.Ref, "capaStatus", capa == null ? null : capa.Status);
            }).ToList();
            bool canEdit = a.LeadAuditorId == c.User.Id || L(c.User) >= 3;
            return O("audit", AuditDto(a), "findings", findings, "perms", O("edit", canEdit && a.Status != "Completed", "reopen", canEdit && a.Status == "Completed"),
                "history", D.Log.Where(l => l.Entity == "Audit" && l.EntityId == a.Id).OrderByDescending(l => l.Id).ToList());
        }

        static object UpdateAudit(Ctx c)
        {
            var a = Find(D.Audits, x => x.Id == c.Id, "Audit");
            Allow(a.LeadAuditorId == c.User.Id || L(c.User) >= 3);
            ApplyAudit(c, a);
            var st = c.S("status");
            if (st != null && st != a.Status)
            {
                Need(st == "Planned" || st == "In Progress" || st == "Completed", "Invalid audit status.");
                if (st == "Completed") Need(!string.IsNullOrWhiteSpace(a.Summary), "Write the audit summary / conclusion before completing the audit.");
                a.Status = st;
                a.CompletedAt = st == "Completed" ? Today() : null;
                Log(c.User, "Audit status changed", "Audit", a.Id, a.Ref + " -> " + st);
            }
            else Log(c.User, "Updated audit", "Audit", a.Id, a.Ref);
            return O("ok", true);
        }

        static object AddFinding(Ctx c)
        {
            var a = Find(D.Audits, x => x.Id == c.Id, "Audit");
            Allow(a.LeadAuditorId == c.User.Id || L(c.User) >= 3);
            Need(a.Status != "Completed", "Reopen the audit before adding findings.");
            var type = c.S("type");
            Need(FindingTypes.Contains(type), "Select the finding type.");
            Need(c.S("description") != null, "Describe the finding.");
            var n = D.Findings.Count(f => f.AuditId == a.Id) + 1;
            var f2 = new Finding
            {
                Id = Store.NextId(), AuditId = a.Id, Ref = a.Ref + "-F" + n.ToString("00"), Type = type,
                Clause = c.S("clause"), Description = c.S("description"), Evidence = c.S("evidence"),
                CreatedBy = c.User.Id, CreatedAt = Now()
            };
            bool raise = type.EndsWith("NC") || c.B("raiseCapa");
            if (raise)
            {
                var owner = UserById(c.I("capaOwnerId"));
                Need(owner != null && owner.Active, "Nonconformities need a corrective action owner.");
                Need(ValidDate(c.S("capaDueDate")), "Nonconformities need a corrective action due date.");
                var capa = NewCapa(c.User, "Internal Audit", f2.Ref + ": " + Trunc(f2.Description, 90), f2.Description,
                    a.Bu, a.Dept, owner.Id, c.S("capaDueDate"));
                capa.FindingId = f2.Id;
                f2.CapaId = capa.Id;
            }
            D.Findings.Add(f2);
            Log(c.User, "Recorded finding", "Audit", a.Id, f2.Ref + " (" + type + ")" + (raise ? " - CAPA raised" : ""));
            return O("id", f2.Id);
        }

        static string Trunc(string s, int n) { return s == null || s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        // ---------- CAPA ----------

        static Capa NewCapa(User by, string source, string title, string desc, string bu, string dept, int ownerId, string due)
        {
            var year = DateTime.Today.Year;
            var n = D.Capas.Count(x => x.Ref.StartsWith("CAPA-" + year + "-")) + 1;
            var capa = new Capa
            {
                Id = Store.NextId(), Ref = "CAPA-" + year + "-" + n.ToString("000"), Source = source, Title = title,
                Description = desc, Bu = bu, Dept = dept, OwnerId = ownerId, DueDate = due, Status = "Open",
                CreatedBy = by.Id, CreatedAt = Now()
            };
            D.Capas.Add(capa);
            Log(by, "Raised CAPA", "CAPA", capa.Id, capa.Ref + " assigned to " + UName(ownerId) + ", due " + due);
            return capa;
        }

        static Dictionary<string, object> CapaDto(Capa x)
        {
            var f = D.Findings.FirstOrDefault(y => y.Id == x.FindingId);
            var a = f == null ? null : D.Audits.FirstOrDefault(y => y.Id == f.AuditId);
            return O("id", x.Id, "ref", x.Ref, "source", x.Source, "title", x.Title, "description", x.Description,
                "bu", x.Bu, "buName", CodeName(D.BusinessUnits, x.Bu), "dept", x.Dept, "deptName", CodeName(D.Departments, x.Dept),
                "ownerId", x.OwnerId, "ownerName", UName(x.OwnerId), "dueDate", x.DueDate,
                "overdue", x.Status != "Closed" && Overdue(x.DueDate),
                "rootCause", x.RootCause, "correction", x.Correction, "correctiveAction", x.CorrectiveAction, "evidence", x.Evidence,
                "status", x.Status, "verifiedByName", UName(x.VerifiedBy), "verifiedAt", x.VerifiedAt,
                "verificationComment", x.VerificationComment, "createdBy", x.CreatedBy, "createdByName", UName(x.CreatedBy),
                "createdAt", x.CreatedAt, "closedAt", x.ClosedAt,
                "findingRef", f == null ? null : f.Ref, "findingType", f == null ? null : f.Type,
                "auditId", a == null ? 0 : a.Id, "auditRef", a == null ? null : a.Ref);
        }

        static object ListCapas(Ctx c)
        {
            IEnumerable<Capa> list = L(c.User) >= 2 ? D.Capas : D.Capas.Where(x => x.OwnerId == c.User.Id);
            var st = c.Qs("status");
            if (st == "overdue") list = list.Where(x => x.Status != "Closed" && Overdue(x.DueDate));
            else if (st == "active") list = list.Where(x => x.Status != "Closed");
            else if (st != null) list = list.Where(x => x.Status == st);
            if (c.Qs("mine") == "1") list = list.Where(x => x.OwnerId == c.User.Id);
            return list.OrderBy(x => x.Status == "Closed").ThenBy(x => x.DueDate).Select(CapaDto).ToList();
        }

        static object CreateCapa(Ctx c)
        {
            Allow(L(c.User) >= 2);
            Need(c.S("title") != null, "Title is required.");
            Need(D.BusinessUnits.Any(x => x.Code == c.S("bu")) && D.Departments.Any(x => x.Code == c.S("dept")), "Select the business unit and department.");
            var owner = UserById(c.I("ownerId"));
            Need(owner != null && owner.Active, "Select the action owner.");
            Need(ValidDate(c.S("dueDate")), "Enter a valid due date.");
            var capa = NewCapa(c.User, c.S("source") ?? "Other", c.S("title"), c.S("description"), c.S("bu"), c.S("dept"), owner.Id, c.S("dueDate"));
            return O("id", capa.Id);
        }

        static object GetCapa(Ctx c)
        {
            var x = Find(D.Capas, y => y.Id == c.Id, "CAPA");
            var u = c.User;
            Allow(L(u) >= 2 || x.OwnerId == u.Id);
            bool isOwner = x.OwnerId == u.Id;
            bool admin = L(u) >= 3 || x.CreatedBy == u.Id;
            return O("capa", CapaDto(x),
                "perms", O("edit", (isOwner || admin) && (x.Status == "Open" || x.Status == "In Progress"),
                           "editHeader", admin && x.Status != "Closed",
                           "submit", (isOwner || L(u) >= 3) && (x.Status == "Open" || x.Status == "In Progress"),
                           "verify", admin && !isOwner && x.Status == "Pending Verification"),
                "history", D.Log.Where(l => l.Entity == "CAPA" && l.EntityId == x.Id).OrderByDescending(l => l.Id).ToList());
        }

        static object UpdateCapa(Ctx c)
        {
            var x = Find(D.Capas, y => y.Id == c.Id, "CAPA");
            var u = c.User;
            bool admin = L(u) >= 3 || x.CreatedBy == u.Id;
            Allow(x.OwnerId == u.Id || admin);
            Need(x.Status == "Open" || x.Status == "In Progress", "This CAPA can no longer be edited.");
            if (c.Has("rootCause")) x.RootCause = c.S("rootCause");
            if (c.Has("correction")) x.Correction = c.S("correction");
            if (c.Has("correctiveAction")) x.CorrectiveAction = c.S("correctiveAction");
            if (c.Has("evidence")) x.Evidence = c.S("evidence");
            if (admin)
            {
                if (c.Has("title")) { Need(c.S("title") != null, "Title is required."); x.Title = c.S("title"); }
                if (c.Has("description")) x.Description = c.S("description");
                if (c.Has("dueDate")) { Need(ValidDate(c.S("dueDate")), "Enter a valid due date."); x.DueDate = c.S("dueDate"); }
                if (c.Has("ownerId"))
                {
                    var o = UserById(c.I("ownerId"));
                    Need(o != null && o.Active, "Select the action owner.");
                    if (o.Id != x.OwnerId) Log(u, "Reassigned CAPA", "CAPA", x.Id, UName(x.OwnerId) + " -> " + o.Name);
                    x.OwnerId = o.Id;
                }
            }
            if (x.Status == "Open" && (x.RootCause != null || x.CorrectiveAction != null)) x.Status = "In Progress";
            Log(u, "Updated CAPA", "CAPA", x.Id, x.Ref);
            return O("ok", true);
        }

        static object SubmitCapa(Ctx c)
        {
            var x = Find(D.Capas, y => y.Id == c.Id, "CAPA");
            Allow(x.OwnerId == c.User.Id || L(c.User) >= 3);
            Need(x.Status == "Open" || x.Status == "In Progress", "This CAPA is not open.");
            Need(!string.IsNullOrWhiteSpace(x.RootCause) && !string.IsNullOrWhiteSpace(x.CorrectiveAction),
                "Record the root cause and the corrective action taken before submitting for verification.");
            x.Status = "Pending Verification";
            Log(c.User, "Submitted for verification", "CAPA", x.Id, x.Ref);
            return O("ok", true);
        }

        static object VerifyCapa(Ctx c)
        {
            var x = Find(D.Capas, y => y.Id == c.Id, "CAPA");
            Allow((L(c.User) >= 3 || x.CreatedBy == c.User.Id) && x.OwnerId != c.User.Id);
            Need(x.Status == "Pending Verification", "This CAPA is not awaiting verification.");
            bool effective = c.B("effective");
            var comment = c.S("comment");
            if (!effective) Need(comment != null, "Explain why the action is not yet effective.");
            x.VerifiedBy = c.User.Id;
            x.VerifiedAt = Now();
            x.VerificationComment = comment;
            x.Status = effective ? "Closed" : "In Progress";
            x.ClosedAt = effective ? Today() : null;
            Log(c.User, effective ? "Verified effective and closed" : "Verification failed - returned to owner", "CAPA", x.Id,
                x.Ref + (comment == null ? "" : ": " + comment));
            return O("ok", true);
        }

        // ---------- reports ----------

        static object Reports(Ctx c)
        {
            Allow(L(c.User) >= 2);
            var docs = D.Documents;
            var effective = docs.Where(d => d.Status == "Effective").ToList();
            var activeUsers = D.Users.Where(u => u.Active).ToList();

            int reqTotal = 0, ackTotal = 0;
            var ackRows = new List<Dictionary<string, object>>();
            foreach (var d in effective)
            {
                var eff = Eff(d);
                var aud = activeUsers.Where(u => Audience(u, d)).ToList();
                var acked = aud.Count(u => HasAcked(u, eff));
                reqTotal += aud.Count; ackTotal += acked;
                ackRows.Add(O("id", d.Id, "docNo", d.DocNo, "title", d.Title, "rev", eff.Rev, "effectiveDate", eff.EffectiveDate,
                    "required", aud.Count, "acknowledged", acked, "pct", aud.Count == 0 ? 100 : (int)Math.Round(100.0 * acked / aud.Count)));
            }

            var working = D.Revisions.Where(r => WorkingStatuses.Contains(r.Status)).ToList();
            return O(
                "kpi", O("effective", effective.Count,
                         "inApproval", working.Count(r => r.Status == "InReview" || r.Status == "Approved"),
                         "drafts", working.Count(r => r.Status == "Draft" || r.Status == "Rejected"),
                         "obsolete", docs.Count(d => d.Status == "Obsolete"),
                         "reviewsOverdue", effective.Count(d => ReviewState(d) == "Overdue"),
                         "ackRate", reqTotal == 0 ? 100 : (int)Math.Round(100.0 * ackTotal / reqTotal),
                         "capaOpen", D.Capas.Count(x => x.Status != "Closed"),
                         "capaOverdue", D.Capas.Count(x => x.Status != "Closed" && Overdue(x.DueDate)),
                         "auditsOpen", D.Audits.Count(a => a.Status != "Completed"),
                         "users", activeUsers.Count),
                "byType", D.DocTypes.Select(t => O("name", t.Name, "count", effective.Count(d => d.Type == t.Code))).ToList(),
                "byBu", D.BusinessUnits.Select(t => O("name", t.Name, "count", effective.Count(d => d.Bu == t.Code))).ToList(),
                "byDept", D.Departments.Select(t => O("name", t.Name, "count", effective.Count(d => d.Dept == t.Code))).Where(x => (int)x["count"] > 0).ToList(),
                "reviews", effective.Where(d => ReviewState(d) != "").OrderBy(d => d.NextReviewDate).Select(d => DocDto(d, c.User)).ToList(),
                "acks", ackRows.OrderBy(r => (int)r["pct"]).ToList(),
                "capaByStatus", new[] { "Open", "In Progress", "Pending Verification", "Closed" }.Select(s => O("name", s, "count", D.Capas.Count(x => x.Status == s))).ToList(),
                "capaOverdue", D.Capas.Where(x => x.Status != "Closed" && Overdue(x.DueDate)).OrderBy(x => x.DueDate).Select(CapaDto).ToList(),
                "findingsByType", FindingTypes.Select(t => O("name", t, "count", D.Findings.Count(f => f.Type == t))).ToList(),
                "audits", D.Audits.OrderByDescending(a => a.PlannedDate).Take(10).Select(AuditDto).ToList());
        }

        // ---------- users ----------

        static object ListUsers(Ctx c)
        {
            Allow(L(c.User) >= 2);
            bool full = L(c.User) >= 4;
            return D.Users.Where(u => full || u.Active).OrderBy(u => u.Name).Select(u => UserDto(u, full)).ToList();
        }

        static void ApplyUser(Ctx c, User u)
        {
            if (c.Has("name")) { Need(c.S("name") != null, "Full name is required."); u.Name = c.S("name"); }
            if (c.Has("email")) u.Email = c.S("email");
            if (c.Has("position")) u.Position = c.S("position");
            if (c.Has("role")) { Need(Roles.Contains(c.S("role")), "Select a valid role."); u.Role = c.S("role"); }
            if (c.Has("bu")) { Need(D.BusinessUnits.Any(x => x.Code == c.S("bu")), "Select the business unit."); u.Bu = c.S("bu"); }
            if (c.Has("dept")) { Need(D.Departments.Any(x => x.Code == c.S("dept")), "Select the department."); u.Dept = c.S("dept"); }
            if (c.Has("active")) u.Active = c.B("active");
        }

        static object CreateUser(Ctx c)
        {
            Allow(L(c.User) >= 4);
            var username = (c.S("username") ?? "").ToLowerInvariant();
            Need(System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9._-]{3,40}$"), "Username must be 3-40 letters, numbers, dots, dashes or underscores.");
            Need(!D.Users.Any(u => u.Username.ToLowerInvariant() == username), "That username is already taken.");
            Need(c.S("name") != null && c.S("role") != null && c.S("bu") != null && c.S("dept") != null, "Name, role, business unit and department are required.");
            CheckPasswordStrength(c.S("password"));
            var u2 = new User { Id = Store.NextId(), Username = username, Active = true, MustChangePassword = true };
            ApplyUser(c, u2);
            u2.Active = true;
            Security.SetPassword(u2, c.S("password"));
            D.Users.Add(u2);
            Log(c.User, "Created user", "User", u2.Id, u2.Username + " (" + u2.Role + ")");
            return O("id", u2.Id);
        }

        static object UpdateUser(Ctx c)
        {
            Allow(L(c.User) >= 4);
            var u = Find(D.Users, x => x.Id == c.Id, "User");
            if (u.Id == c.User.Id)
            {
                Need(!c.Has("active") || c.B("active"), "You cannot deactivate your own account.");
                Need(!c.Has("role") || c.S("role") == "Admin", "You cannot remove your own administrator role.");
            }
            var before = u.Role + "/" + u.Bu + "/" + u.Dept + "/" + (u.Active ? "active" : "inactive");
            ApplyUser(c, u);
            if (c.B("unlock")) { u.LockedUntil = null; u.FailedLogins = 0; }
            if (!u.Active) foreach (var k in Program.Sessions.Where(s => s.Value.UserId == u.Id).Select(s => s.Key).ToList()) Program.Sessions.Remove(k);
            Log(c.User, "Updated user", "User", u.Id, u.Username + ": " + before + " -> " + u.Role + "/" + u.Bu + "/" + u.Dept + "/" + (u.Active ? "active" : "inactive"));
            return O("ok", true);
        }

        static object ResetPassword(Ctx c)
        {
            Allow(L(c.User) >= 4);
            var u = Find(D.Users, x => x.Id == c.Id, "User");
            CheckPasswordStrength(c.S("password"));
            Security.SetPassword(u, c.S("password"));
            u.MustChangePassword = true;
            u.LockedUntil = null;
            u.FailedLogins = 0;
            Log(c.User, "Reset password", "User", u.Id, u.Username);
            return O("ok", true);
        }

        static object AuditTrail(Ctx c)
        {
            Allow(L(c.User) >= 3);
            IEnumerable<LogEntry> list = D.Log;
            var q = c.Qs("q");
            if (q != null)
            {
                q = q.ToLowerInvariant();
                list = list.Where(l => (l.UserName + " " + l.Action + " " + l.Entity + " " + l.Detail).ToLowerInvariant().Contains(q));
            }
            var entity = c.Qs("entity"); if (entity != null) list = list.Where(l => l.Entity == entity);
            return list.OrderByDescending(l => l.Id).Take(500).Select(l =>
            {
                string label = null;
                if (l.Entity == "Document") { var d = D.Documents.FirstOrDefault(x => x.Id == l.EntityId); label = d == null ? null : d.DocNo; }
                else if (l.Entity == "Audit") { var a = D.Audits.FirstOrDefault(x => x.Id == l.EntityId); label = a == null ? null : a.Ref; }
                else if (l.Entity == "CAPA") { var x2 = D.Capas.FirstOrDefault(x => x.Id == l.EntityId); label = x2 == null ? null : x2.Ref; }
                return O("id", l.Id, "at", l.At, "userName", l.UserName, "action", l.Action, "entity", l.Entity, "entityId", l.EntityId, "label", label, "detail", l.Detail);
            }).ToList();
        }
    }
}
