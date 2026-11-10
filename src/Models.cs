using System.Collections.Generic;

namespace LeonioIms
{
    public class NamedCode
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }          // Employee | Contributor | Controller | Admin
        public string Bu { get; set; }
        public string Dept { get; set; }
        public string Position { get; set; }
        public string PasswordHash { get; set; }
        public string Salt { get; set; }
        public bool Active { get; set; }
        public bool MustChangePassword { get; set; }
        public int FailedLogins { get; set; }
        public string LockedUntil { get; set; }
    }

    public class Doc
    {
        public int Id { get; set; }
        public string DocNo { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string Bu { get; set; }
        public string Dept { get; set; }
        public int OwnerId { get; set; }
        public int ApproverId { get; set; }
        public string Standards { get; set; }
        public string Classification { get; set; } // Internal | Restricted
        public string Process { get; set; }
        public int ReviewMonths { get; set; }
        public string NextReviewDate { get; set; }
        public string Status { get; set; }         // Draft | Effective | Obsolete
        public int EffectiveRevisionId { get; set; }
        public string ObsoleteReason { get; set; }
        public string CreatedAt { get; set; }
    }

    public class Revision
    {
        public int Id { get; set; }
        public int DocId { get; set; }
        public string Rev { get; set; }
        public string Status { get; set; }         // Draft | InReview | Approved | Rejected | Effective | Superseded | Withdrawn
        public string Content { get; set; }
        public string ChangeSummary { get; set; }
        public string FileId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public int CreatedBy { get; set; }
        public string CreatedAt { get; set; }
        public int SubmittedBy { get; set; }
        public string SubmittedAt { get; set; }
        public int ReviewedBy { get; set; }
        public string ReviewedAt { get; set; }
        public string ReviewComment { get; set; }
        public int PublishedBy { get; set; }
        public string EffectiveDate { get; set; }
        public string SupersededAt { get; set; }
    }

    public class Ack
    {
        public int Id { get; set; }
        public int RevisionId { get; set; }
        public int UserId { get; set; }
        public string At { get; set; }
    }

    public class Audit
    {
        public int Id { get; set; }
        public string Ref { get; set; }
        public string Title { get; set; }
        public string Standard { get; set; }
        public string Bu { get; set; }
        public string Dept { get; set; }
        public string Scope { get; set; }
        public int LeadAuditorId { get; set; }
        public string PlannedDate { get; set; }
        public string Status { get; set; }         // Planned | In Progress | Completed
        public string Summary { get; set; }
        public string CompletedAt { get; set; }
        public int CreatedBy { get; set; }
        public string CreatedAt { get; set; }
    }

    public class Finding
    {
        public int Id { get; set; }
        public int AuditId { get; set; }
        public string Ref { get; set; }
        public string Type { get; set; }           // Major NC | Minor NC | Observation | Opportunity for Improvement
        public string Clause { get; set; }
        public string Description { get; set; }
        public string Evidence { get; set; }
        public int CapaId { get; set; }
        public int CreatedBy { get; set; }
        public string CreatedAt { get; set; }
    }

    public class Capa
    {
        public int Id { get; set; }
        public string Ref { get; set; }
        public string Source { get; set; }
        public int FindingId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Bu { get; set; }
        public string Dept { get; set; }
        public int OwnerId { get; set; }
        public string DueDate { get; set; }
        public string RootCause { get; set; }
        public string Correction { get; set; }
        public string CorrectiveAction { get; set; }
        public string Evidence { get; set; }
        public string Status { get; set; }         // Open | In Progress | Pending Verification | Closed
        public int VerifiedBy { get; set; }
        public string VerifiedAt { get; set; }
        public string VerificationComment { get; set; }
        public int CreatedBy { get; set; }
        public string CreatedAt { get; set; }
        public string ClosedAt { get; set; }
    }

    public class LogEntry
    {
        public int Id { get; set; }
        public string At { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string Action { get; set; }
        public string Entity { get; set; }
        public int EntityId { get; set; }
        public string Detail { get; set; }
    }

    public class Db
    {
        public int Seq { get; set; }
        public List<NamedCode> BusinessUnits { get; set; }
        public List<NamedCode> Departments { get; set; }
        public List<NamedCode> DocTypes { get; set; }
        public List<User> Users { get; set; }
        public List<Doc> Documents { get; set; }
        public List<Revision> Revisions { get; set; }
        public List<Ack> Acks { get; set; }
        public List<Audit> Audits { get; set; }
        public List<Finding> Findings { get; set; }
        public List<Capa> Capas { get; set; }
        public List<LogEntry> Log { get; set; }

        public Db()
        {
            BusinessUnits = new List<NamedCode>();
            Departments = new List<NamedCode>();
            DocTypes = new List<NamedCode>();
            Users = new List<User>();
            Documents = new List<Doc>();
            Revisions = new List<Revision>();
            Acks = new List<Ack>();
            Audits = new List<Audit>();
            Findings = new List<Finding>();
            Capas = new List<Capa>();
            Log = new List<LogEntry>();
        }
    }
}
