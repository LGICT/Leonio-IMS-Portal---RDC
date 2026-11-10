# Leonio Group IMS Portal

A self-hosted **Integrated Management System (IMS) portal** for controlled policies, procedures, work instructions and forms. It also covers internal audits, corrective and preventive actions (CAPA), acknowledgements and IMS reporting.

It runs entirely on your own computer or server. **No installation, internet connection or database server is needed.** It is built with the C# compiler and .NET Framework that already come with Windows 10/11 and Windows Server.

---

## Start it

1. Double-click **`start.cmd`**.
   - On first run it builds `LeonioIMS.exe` and creates the database with demo data.
   - Your browser opens at **http://localhost:8080**.
2. Keep the black console window open while you use the portal. Close it or press `Ctrl+C` to stop.

If port 8080 is already used, run `LeonioIMS.exe --port 8090` and open `http://localhost:8090`.

## Demo accounts

Every demo account uses the password **`Leonio@2026`**. Change or deactivate these accounts before real use.

| Username | Name | Role | Use it to try… |
|---|---|---|---|
| `admin` | System Administrator | Admin | Users & access, everything |
| `dcontroller` | Ana Dela Cruz | Controller | Publishing approved revisions, audit trail, CAPA verification |
| `vnavarro` | Victor Navarro (COO) | Contributor | **Approving** Procurement Procedure Rev 03 (waiting in My Tasks) |
| `jsantos` | Jose Santos | Contributor | Management Systems Manager: owner of IMS procedures, has a draft |
| `mreyes` | Maria Reyes | Contributor | Procurement owner with an open CAPA |
| `cmendoza` | Carlo Mendoza | Contributor | Internal auditor: audits, findings, raising CAPA |
| `ncastillo` | Nestor Castillo | Contributor | Shipping fleet operations, CAPA pending verification |
| `ptorres` | Paolo Torres | Employee | Shipping officer: reads and acknowledges procedures |
| `kvillanueva` | Kristine Villanueva | Employee | Mining safety officer: sees Mining documents only |
| `gaquino` | Grace Aquino | Employee | Land BU: has an **overdue** CAPA |

To start again with fresh demo data, stop the portal and delete the `data` folder.

---

## What it does

### Employee procedure portal
- Home page with search, categories (Policies, Procedures, Work Instructions, Forms, Manuals) and recently published documents.
- Full-text search across document number, title, process, standards and content.
- Each employee sees **only the documents that apply to them**:
  - Group-wide documents apply to everyone.
  - Business-unit documents (Corporate, Shipping, Mining, Land) apply only to that business unit.
  - Restricted documents are limited to the owning department.
- Only the **current effective revision** is shown. Drafts and superseded content are hidden from ordinary employees.
- Pages print with an "UNCONTROLLED WHEN PRINTED" footer showing who printed the page and when.

### Document control (ISO 9001/14001/45001 clause 7.5)
- Automatic numbering in the form `BU-DEPT-TYPE-NNN`, for example `GRP-PRC-PRO-001`.
- Lifecycle: **Draft → In approval → Approved → Effective → Superseded / Obsolete**, with a "Returned for correction" loop.
- **Segregation of duties:**
  - The approver can never be the person who prepared the revision.
  - Only a Document Controller can publish.
  - Nothing can be published without approval.
- Metadata on each document: owner, approver, applicable standards and clauses, related process, classification, review cycle and next review date.
- Optional attached file (PDF, Word or Excel, up to 25 MB) per revision.
- Full revision history with who prepared, approved and published each revision, and when.
- Periodic review tracking with "due soon" and "overdue" flags.

### Acknowledgements
- When a revision is published, every employee covered by it must acknowledge it.
- The acknowledgement status per document lists who has and has not read it.

### Internal audits (clause 9.2)
- Audit programme with lead auditor, scope, standard and status (Planned → In Progress → Completed).
- Findings classified as Major NC, Minor NC, Observation or Opportunity for Improvement.
- Every nonconformity **automatically raises a CAPA** with an owner and a due date.

### CAPA (clause 10.2)
- Open → In Progress → Pending Verification → Closed.
- The owner records the root cause, the immediate correction, the corrective action and the evidence.
- **Effectiveness verification** is done by someone other than the owner. They close the CAPA or return it to the owner.
- Overdue tracking.

### Reports and audit trail
- KPIs:
  - effective documents
  - documents in approval
  - overdue reviews
  - acknowledgement rate
  - open and overdue CAPA
  - open audits
- Charts by document type, by business unit, by CAPA status and by finding type.
- Acknowledgement compliance per document.
- An audit trail of every sign-in, change, approval, publication, acknowledgement and CAPA step. Entries cannot be edited from the portal.

### Users & access
| Role | Can |
|---|---|
| **Employee** | Search, read, download and acknowledge applicable documents; work on CAPA assigned to them |
| **Contributor** (process owner / approver / auditor) | Create and revise documents they own, approve revisions assigned to them, run audits, raise CAPA, view reports |
| **Controller** (IMS document controller) | Publish approved revisions, make documents obsolete, manage all documents, verify CAPA, view the audit trail |
| **Admin** | Everything, plus user management |

Security measures:
- Passwords are stored as salted PBKDF2 hashes.
- An account locks for 15 minutes after 5 failed sign-ins.
- New and reset passwords must be changed at the next sign-in.
- Sessions expire after 8 hours without activity.

---

## Files

```
start.cmd         Start the portal (builds it the first time)
build.cmd         Rebuild LeonioIMS.exe after changing the source code
LeonioIMS.exe     The portal server (created by build.cmd)
src/              C# server source: models, API, workflow rules, demo data
wwwroot/          Web interface (HTML, CSS, JavaScript)
data/             Created on first run: database and attachments
  ims-db.json     All records (users, documents, revisions, audits, CAPA, audit trail)
  ims-db.json.bak Previous copy, kept automatically on every save
  files/          Uploaded document attachments
```

**Back up the whole `data` folder.** It holds all your records.

## Customising

- **Business units, departments and document types** are stored in `data/ims-db.json` under `BusinessUnits`, `Departments` and `DocTypes`. Stop the portal before editing that file.
- **Demo data** is defined in `src/Seed.cs`.
- After changing anything in `src/`, run `build.cmd`. Changes in `wwwroot/` only need a browser refresh.

---

## Moving from this PC to a company server (later)

This version is set up for **local use on one computer**: it only accepts connections from `localhost`. To give all Leonio Group employees access, the recommended path is:

1. **Host it on a Windows Server.**
   - Change the listener in `src/Server.cs` from `http://localhost:PORT/` to `http://+:PORT/`.
   - Reserve the URL once, as administrator: `netsh http add urlacl url=http://+:8080/ user=DOMAIN\svc-ims`.
   - Run the portal as a Windows service, for example with NSSM or a scheduled task.
2. **Use HTTPS** with a company certificate, either by binding it with `netsh http add sslcert` or by placing IIS or a reverse proxy in front.
3. **Single sign-on:** the built-in web server (`HttpListener`) supports Windows Integrated Authentication, so domain users could sign in with their Active Directory accounts.
4. **Database:** the JSON file store suits a pilot of a few hundred users. For Group-wide production use, move storage to SQL Server; all data access goes through `Store` in `Server.cs`.
5. **Remote sites:** decide how vessels and mine sites with intermittent connectivity will reach the server, through VPN access or a cached offline copy.
6. **Backups:** take daily backups of the `data` folder, keep an off-site copy, and test restores regularly.

## Limitations of this version
- Data is a single JSON file loaded in memory. It is fine for thousands of documents but is not a multi-server database.
- There are no email notifications yet; employees see pending items in **My Tasks** and on the home page.
- Document content is written in simple Markdown, or attached as a file. There is no in-browser Word editing.
- Risk register, training/competence records and management review (P2 modules from the evaluation) are not yet included.
