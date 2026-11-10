using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LeonioIms
{
    // Demo data created on first run only. Delete data\ims-db.json to start again,
    // or replace the demo users and documents with real ones through the portal.
    public static class Seed
    {
        public const string DemoPassword = "Leonio@2026";
        static Db D { get { return Store.Db; } }

        static string Day(int offset) { return DateTime.Today.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        static string Stamp(int offset) { return Day(offset) + " 09:00"; }

        static NamedCode N(string code, string name) { return new NamedCode { Code = code, Name = name }; }

        static User U(string username, string name, string role, string bu, string dept, string position)
        {
            var u = new User
            {
                Id = Store.NextId(), Username = username, Name = name, Role = role, Bu = bu, Dept = dept,
                Position = position, Active = true, Email = username + "@leonio.local"
            };
            Security.SetPassword(u, DemoPassword);
            D.Users.Add(u);
            return u;
        }

        static Doc Doc(string bu, string dept, string type, string title, User owner, User approver, string standards,
                       string process, int reviewMonths, string content, int publishedDaysAgo, params string[] history)
        {
            var prefix = bu + "-" + dept + "-" + type + "-";
            var d = new Doc
            {
                Id = Store.NextId(), DocNo = prefix + (D.Documents.Count(x => x.DocNo.StartsWith(prefix)) + 1).ToString("000"),
                Title = title, Type = type, Bu = bu, Dept = dept, OwnerId = owner.Id, ApproverId = approver.Id,
                Standards = standards, Process = process, ReviewMonths = reviewMonths, Classification = "Internal",
                Status = "Effective", CreatedAt = Stamp(-publishedDaysAgo - 400)
            };
            D.Documents.Add(d);
            var controller = D.Users.First(x => x.Role == "Controller");
            // Older superseded revisions
            for (int i = 0; i < history.Length; i++)
            {
                int age = publishedDaysAgo + (history.Length - i) * 365;
                D.Revisions.Add(new Revision
                {
                    Id = Store.NextId(), DocId = d.Id, Rev = i.ToString("00"), Status = "Superseded", Content = content,
                    ChangeSummary = history[i], CreatedBy = owner.Id, CreatedAt = Stamp(-age - 20), SubmittedBy = owner.Id,
                    SubmittedAt = Stamp(-age - 10), ReviewedBy = approver.Id, ReviewedAt = Stamp(-age - 3), PublishedBy = controller.Id,
                    EffectiveDate = Day(-age), SupersededAt = Day(-age + 365)
                });
            }
            var r = new Revision
            {
                Id = Store.NextId(), DocId = d.Id, Rev = history.Length.ToString("00"), Status = "Effective", Content = content,
                ChangeSummary = history.Length == 0 ? "Initial issue" : "Periodic review and update",
                CreatedBy = owner.Id, CreatedAt = Stamp(-publishedDaysAgo - 20), SubmittedBy = owner.Id, SubmittedAt = Stamp(-publishedDaysAgo - 10),
                ReviewedBy = approver.Id, ReviewedAt = Stamp(-publishedDaysAgo - 3), PublishedBy = controller.Id, EffectiveDate = Day(-publishedDaysAgo)
            };
            D.Revisions.Add(r);
            d.EffectiveRevisionId = r.Id;
            d.NextReviewDate = DateTime.Today.AddDays(-publishedDaysAgo).AddMonths(reviewMonths).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Api.Log(controller, "Published revision", "Document", d.Id, "Rev " + r.Rev + " effective " + r.EffectiveDate + " (migrated)");
            return d;
        }

        static void AckAll(Doc d, double share)
        {
            var eff = D.Revisions.First(r => r.Id == d.EffectiveRevisionId);
            var audience = D.Users.Where(u => Api.Audience(u, d)).ToList();
            int n = (int)Math.Round(audience.Count * share);
            foreach (var u in audience.Take(n))
                D.Acks.Add(new Ack { Id = Store.NextId(), RevisionId = eff.Id, UserId = u.Id, At = Stamp(-1) });
        }

        public static void Run()
        {
            D.BusinessUnits = new List<NamedCode> {
                N("GRP", "Group-wide"), N("CORP", "Corporate / Shared Services"), N("SHP", "Shipping"), N("MIN", "Mining"), N("LND", "Land") };
            D.Departments = new List<NamedCode> {
                N("ADM", "Administration"), N("FIN", "Finance"), N("HR", "Human Resources"), N("HSE", "Health, Safety & Environment"),
                N("ICT", "Information & Communications Technology"), N("OPS", "Operations"), N("PRC", "Procurement"), N("QMS", "Management Systems") };
            D.DocTypes = new List<NamedCode> {
                N("POL", "Policy"), N("PRO", "Procedure"), N("WI", "Work Instruction"), N("FRM", "Form / Template"), N("MAN", "Manual") };

            var admin = U("admin", "System Administrator", "Admin", "CORP", "ICT", "ICT Systems Administrator");
            var controller = U("dcontroller", "Ana Dela Cruz", "Controller", "CORP", "QMS", "IMS Document Controller");
            var coo = U("vnavarro", "Victor Navarro", "Contributor", "CORP", "ADM", "Chief Operating Officer");
            var ims = U("jsantos", "Jose Santos", "Contributor", "CORP", "QMS", "Management Systems Manager");
            var proc = U("mreyes", "Maria Reyes", "Contributor", "CORP", "PRC", "Procurement Manager");
            var ict = U("rgarcia", "Ramon Garcia", "Contributor", "CORP", "ICT", "ICT Manager");
            var hr = U("lbautista", "Liza Bautista", "Contributor", "CORP", "HR", "HR Manager");
            var auditor = U("cmendoza", "Carlo Mendoza", "Contributor", "CORP", "QMS", "Internal Auditor");
            var fleet = U("ncastillo", "Nestor Castillo", "Contributor", "SHP", "OPS", "Fleet Operations Manager");
            var mine = U("eramos", "Eduardo Ramos", "Contributor", "MIN", "OPS", "Mine Operations Manager");
            var officer = U("ptorres", "Paolo Torres", "Employee", "SHP", "OPS", "Second Officer");
            var safety = U("kvillanueva", "Kristine Villanueva", "Employee", "MIN", "HSE", "Site Safety Officer");
            var ap = U("gaquino", "Grace Aquino", "Employee", "LND", "FIN", "Accounts Payable Specialist");
            U("jlim", "Jerome Lim", "Employee", "LND", "OPS", "Site Supervisor");
            U("rdomingo", "Rhea Domingo", "Employee", "CORP", "FIN", "Finance Analyst");

            var policy = Doc("GRP", "QMS", "POL", "Integrated Management System Policy", ims, coo,
                "ISO 9001:2015 5.2; ISO 14001:2015 5.2; ISO 45001:2018 5.2", "Leadership & Governance", 24, @"# Purpose
This policy sets out Leonio Group's commitment to quality, environmental protection and occupational health and safety across all business units.

# Policy statement
Leonio Group commits to:
- Deliver services that meet customer, legal and regulatory requirements.
- Prevent work-related injury and ill health and provide safe and healthy workplaces.
- Protect the environment, including prevention of pollution, at sea, at mine sites and on land.
- Consult and involve workers and their representatives.
- Set measurable objectives and continually improve the Integrated Management System (IMS).

# Responsibilities
- **Executive management** provides resources and reviews IMS performance at least annually.
- **Business unit heads** implement this policy within their operations.
- **All employees** follow approved procedures and report hazards, incidents and nonconformities.

# Communication
This policy is published in the IMS Portal, displayed at all sites and vessels, and made available to interested parties on request.", 200, "Initial issue");
            AckAll(policy, 0.85);

            var docControl = Doc("GRP", "QMS", "PRO", "Document Control Procedure", ims, coo,
                "ISO 9001:2015 7.5; ISO 14001:2015 7.5; ISO 45001:2018 7.5", "IMS Document Control", 24, @"# Purpose
Defines how controlled documents are created, reviewed, approved, published, revised and withdrawn.

# Scope
All policies, procedures, work instructions, forms and manuals of Leonio Group and its business units.

# Document numbering
Documents are numbered **BU-DEPT-TYPE-NNN**, for example GRP-PRC-PRO-001.

| Type | Code |
|---|---|
| Policy | POL |
| Procedure | PRO |
| Work Instruction | WI |
| Form / Template | FRM |
| Manual | MAN |

# Procedure
1. The document owner prepares a draft in the IMS Portal.
2. The owner submits the draft to the designated approver. The preparer may not approve their own document.
3. The approver approves the revision or returns it with comments.
4. The IMS Document Controller publishes the approved revision. The previous revision is automatically superseded.
5. Affected employees acknowledge that they have read the new revision.
6. The owner reviews each document at least every 24 months.

# Uncontrolled copies
Printed or downloaded copies are uncontrolled. Always confirm the current revision in the IMS Portal.", 120, "Initial issue");
            AckAll(docControl, 0.7);

            var procurement = Doc("GRP", "PRC", "PRO", "Procurement Procedure", proc, coo,
                "ISO 9001:2015 8.4", "Procure-to-Pay", 24, @"# Purpose
Ensures goods and services are purchased competitively, from approved suppliers, and within the approved Limits of Authority (LOA).

# Procedure
1. The requesting department raises a **Purchase Requisition** (GRP-PRC-FRM-001).
2. The department head approves the requisition according to the LOA.
3. Procurement obtains at least **three quotations** for purchases above PHP 50,000.
4. Procurement evaluates quotations on price, quality, delivery and supplier performance.
5. The Purchase Order is approved according to the LOA and issued to the supplier.
6. The receiving department inspects and accepts delivered goods or services.

# Approved suppliers
Only suppliers on the Approved Supplier List may be used, unless an exception is approved by the COO.

# Records
Requisitions, quotations, evaluations and purchase orders are retained for at least 10 years.", 300, "Initial issue", "Added LOA references");
            AckAll(procurement, 0.6);
            // Revision 03 awaiting approval by the COO
            var procRev = new Revision
            {
                Id = Store.NextId(), DocId = procurement.Id, Rev = "03", Status = "InReview",
                Content = D.Revisions.First(r => r.Id == procurement.EffectiveRevisionId).Content.Replace("above PHP 50,000", "above PHP 100,000") +
                    "\n\n# Emergency purchases\nEmergency purchases for vessel or mine-site safety may proceed with one quotation, subject to approval by the BU head and documentation within 5 working days.",
                ChangeSummary = "Raised the three-quotation threshold to PHP 100,000 and added an emergency purchase provision.",
                CreatedBy = proc.Id, CreatedAt = Stamp(-6), SubmittedBy = proc.Id, SubmittedAt = Stamp(-2)
            };
            D.Revisions.Add(procRev);
            Api.Log(proc, "Submitted for approval", "Document", procurement.Id, "Rev 03 sent to " + coo.Name);

            var access = Doc("GRP", "ICT", "PRO", "IT Access Management Procedure", ict, ims,
                "ISO/IEC 27001:2022 A.5.15-A.5.18", "ICT Service Management", 12, @"# Purpose
Controls the granting, review and removal of access to Leonio Group information systems.

# Procedure
1. Access requests are raised through the ICT service desk and approved by the employee's department head.
2. Access is granted on a least-privilege basis.
3. HR notifies ICT of movers and leavers. Leaver access is removed **on the last working day**.
4. Privileged accounts require multi-factor authentication.
5. Department heads review user access every six months.", 400, "Initial issue");
            AckAll(access, 0.9);

            var onboarding = Doc("GRP", "HR", "PRO", "Employee Onboarding Procedure", hr, coo,
                "ISO 9001:2015 7.2, 7.3", "Hire-to-Retire", 24, @"# Purpose
Ensures new employees are prepared, inducted and competent for their roles.

# Procedure
1. HR issues the onboarding checklist to the hiring manager at least 5 working days before the start date.
2. ICT prepares accounts and equipment.
3. On day one, the employee completes the IMS induction, including the IMS Policy and HSE rules.
4. The hiring manager assigns role-specific procedures in the IMS Portal for acknowledgement.
5. Competence is confirmed at the end of probation.", 45, "Initial issue");
            AckAll(onboarding, 0.4);

            var auditProc = Doc("GRP", "QMS", "PRO", "Internal Audit Procedure", ims, coo,
                "ISO 9001:2015 9.2; ISO 19011:2018", "Performance Evaluation", 24, @"# Purpose
Defines how internal audits of the IMS are planned, conducted and reported.

# Procedure
1. The Management Systems Manager prepares an annual risk-based audit programme.
2. Auditors are independent of the area audited.
3. Findings are classified as **Major NC**, **Minor NC**, **Observation** or **Opportunity for Improvement**.
4. Every nonconformity is recorded in the IMS Portal and a CAPA is raised with an owner and due date.
5. The audit report is completed within 10 working days of the closing meeting.", 150, "Initial issue");
            AckAll(auditProc, 0.5);

            var bunkering = Doc("SHP", "OPS", "WI", "Bunkering Operations Work Instruction", fleet, ims,
                "ISO 14001:2015 8.1, 8.2; MARPOL Annex I", "Vessel Operations", 12, @"# Purpose
Prevents oil spills and injuries during bunkering.

# Before bunkering
1. Complete the bunkering safety checklist with the supplier.
2. Plug all deck scuppers and prepare the SOPEP equipment.
3. Agree on the transfer rate and the emergency stop signal.

# During bunkering
- Keep continuous watch at the manifold.
- Monitor tank levels; top-off at reduced rate.

# After bunkering
- Drain and blank the hoses, and record quantities in the Oil Record Book.", 60, "Initial issue");
            AckAll(bunkering, 0.5);

            var ppe = Doc("MIN", "HSE", "WI", "PPE Requirements at Mine Sites", mine, ims,
                "ISO 45001:2018 8.1.2", "Site Safety", 12, @"# Minimum PPE in all operational areas
- Hard hat, safety glasses and high-visibility vest
- Steel-toed safety boots
- Hearing protection where noise exceeds 85 dB(A)

# Task-specific PPE
| Task | Additional PPE |
|---|---|
| Blasting | Blast-rated hearing protection, face shield |
| Working at height | Full-body harness with double lanyard |
| Chemical handling | Chemical gloves and goggles |

Employees who are not wearing the required PPE must not enter operational areas.", 20, "Initial issue");

            Doc("GRP", "PRC", "FRM", "Purchase Requisition Form", proc, coo, "ISO 9001:2015 8.4", "Procure-to-Pay", 24, @"# Form fields
- Requesting department and cost centre
- Item description, specification and quantity
- Estimated cost and budget reference
- Required delivery date
- Requested by / approved by (per LOA)

Use the latest form from the IMS Portal. Older versions will not be accepted by Procurement.", 300);

            // A brand-new draft procedure, not yet submitted
            var draft = new Doc
            {
                Id = Store.NextId(), DocNo = "GRP-HSE-PRO-001", Title = "Incident Reporting and Investigation Procedure", Type = "PRO",
                Bu = "GRP", Dept = "HSE", OwnerId = ims.Id, ApproverId = coo.Id, Standards = "ISO 45001:2018 10.2",
                Process = "Incident Management", ReviewMonths = 24, Classification = "Internal", Status = "Draft", CreatedAt = Stamp(-3)
            };
            D.Documents.Add(draft);
            D.Revisions.Add(new Revision
            {
                Id = Store.NextId(), DocId = draft.Id, Rev = "00", Status = "Draft", ChangeSummary = "Initial issue", CreatedBy = ims.Id, CreatedAt = Stamp(-3),
                Content = "# Purpose\nDefines how incidents and near misses are reported and investigated.\n\n# Procedure\n1. Report every incident to the supervisor immediately.\n2. Record the incident in the IMS Portal within 24 hours.\n3. ..."
            });
            Api.Log(ims, "Created document", "Document", draft.Id, "GRP-HSE-PRO-001 draft");

            // Internal audit with findings and CAPAs
            var a1 = new Audit
            {
                Id = Store.NextId(), Ref = "IA-" + DateTime.Today.Year + "-001", Title = "Procure-to-Pay process audit",
                Standard = "ISO 9001:2015", Bu = "CORP", Dept = "PRC", Scope = "Requisition, quotation, PO approval and receiving",
                LeadAuditorId = auditor.Id, PlannedDate = Day(-40), Status = "Completed", CompletedAt = Day(-30), CreatedBy = auditor.Id, CreatedAt = Stamp(-60),
                Summary = "The procurement process is generally effective. Two nonconformities were raised on LOA compliance and use of obsolete forms."
            };
            D.Audits.Add(a1);
            var f1 = new Finding
            {
                Id = Store.NextId(), AuditId = a1.Id, Ref = a1.Ref + "-F01", Type = "Minor NC", Clause = "8.4.1",
                Description = "3 of 25 sampled purchase orders above PHP 500,000 were approved by the Procurement Manager instead of the COO as required by the LOA.",
                Evidence = "PO-2026-0412, PO-2026-0519, PO-2026-0533", CreatedBy = auditor.Id, CreatedAt = Stamp(-38)
            };
            var f2 = new Finding
            {
                Id = Store.NextId(), AuditId = a1.Id, Ref = a1.Ref + "-F02", Type = "Minor NC", Clause = "7.5.3",
                Description = "Land BU is still using an obsolete version of the Purchase Requisition Form.",
                Evidence = "PR-LND-0088 and PR-LND-0091 used the 2023 form layout.", CreatedBy = auditor.Id, CreatedAt = Stamp(-38)
            };
            var f3 = new Finding
            {
                Id = Store.NextId(), AuditId = a1.Id, Ref = a1.Ref + "-F03", Type = "Opportunity for Improvement", Clause = "8.4.2",
                Description = "Supplier performance evaluation could be automated using delivery data from the ERP.", CreatedBy = auditor.Id, CreatedAt = Stamp(-38)
            };
            D.Findings.AddRange(new[] { f1, f2, f3 });
            var c1 = new Capa
            {
                Id = Store.NextId(), Ref = "CAPA-" + DateTime.Today.Year + "-001", Source = "Internal Audit", FindingId = f1.Id,
                Title = f1.Ref + ": PO approvals not in line with LOA", Description = f1.Description, Bu = "CORP", Dept = "PRC",
                OwnerId = proc.Id, DueDate = Day(20), Status = "In Progress", CreatedBy = auditor.Id, CreatedAt = Stamp(-38),
                RootCause = "The ERP approval matrix was not updated when the LOA was revised.",
                Correction = "The three POs were retroactively reviewed and ratified by the COO."
            };
            var c2 = new Capa
            {
                Id = Store.NextId(), Ref = "CAPA-" + DateTime.Today.Year + "-002", Source = "Internal Audit", FindingId = f2.Id,
                Title = f2.Ref + ": Obsolete requisition form in use", Description = f2.Description, Bu = "LND", Dept = "FIN",
                OwnerId = ap.Id, DueDate = Day(-10), Status = "Open", CreatedBy = auditor.Id, CreatedAt = Stamp(-38)
            };
            var c3 = new Capa
            {
                Id = Store.NextId(), Ref = "CAPA-" + DateTime.Today.Year + "-003", Source = "Incident", Title = "Hydraulic oil leak during bunkering on MV Leonio Star",
                Description = "Approx. 5 litres of hydraulic oil spilled on deck; contained, no discharge overboard.", Bu = "SHP", Dept = "OPS",
                OwnerId = fleet.Id, DueDate = Day(5), Status = "Pending Verification", CreatedBy = ims.Id, CreatedAt = Stamp(-25),
                RootCause = "Worn hose coupling not identified because the pre-bunkering checklist had no hose inspection step.",
                Correction = "Spill cleaned up and hose replaced.",
                CorrectiveAction = "Hose inspection step added to the bunkering checklist; all vessels briefed.",
                Evidence = "Revised checklist and crew briefing attendance sheets for 6 vessels."
            };
            f1.CapaId = c1.Id; f2.CapaId = c2.Id;
            D.Capas.AddRange(new[] { c1, c2, c3 });
            Api.Log(auditor, "Raised CAPA", "CAPA", c1.Id, c1.Ref + " assigned to " + proc.Name);
            Api.Log(auditor, "Raised CAPA", "CAPA", c2.Id, c2.Ref + " assigned to " + ap.Name);
            Api.Log(ims, "Raised CAPA", "CAPA", c3.Id, c3.Ref + " assigned to " + fleet.Name);
            Api.Log(fleet, "Submitted for verification", "CAPA", c3.Id, c3.Ref);

            D.Audits.Add(new Audit
            {
                Id = Store.NextId(), Ref = "IA-" + DateTime.Today.Year + "-002", Title = "Vessel operations and bunkering audit",
                Standard = "ISO 14001:2015; ISO 45001:2018", Bu = "SHP", Dept = "OPS", Scope = "Bunkering, spill response and permit-to-work on two vessels",
                LeadAuditorId = auditor.Id, PlannedDate = Day(14), Status = "Planned", CreatedBy = auditor.Id, CreatedAt = Stamp(-5)
            });

            Api.Log(null, "Database initialised", "System", 0, "Demo data loaded");
        }
    }
}
