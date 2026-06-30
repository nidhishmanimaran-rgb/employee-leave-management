# Employee Leave Management System

An ASP.NET Core MVC (.NET 8) leave request application with HR review, SQL Server persistence, local fallback storage, and SMTP email notifications using MailKit.

## Features

- Employee leave request form with Employee ID, role, department, and phone.
- HR notification email when a leave request is submitted.
- HR dashboard with login.
- HR dashboard employee search by Employee ID, focused employee profile, and summary with total requests, accepted leaves, rejected requests, pending requests, and approved leave days.
- HR dashboard reset action for clearing local leave request and decision data.
- HR can accept or reject leave requests.
- Acceptance or rejection email is sent to the employee.
- Public holiday lookup through the Nager.Date external HTTP API.
- SQL Server storage using ADO.NET when `SQLEXPRESS` is available.
- Local JSONL backup storage when SQL Server is unavailable in Development mode.

## Current Workflow

1. Employee opens the home page and submits a leave request with Employee ID and role details.
2. The app tries to save the request in SQL Server.
3. If SQL Server is unavailable and Development fallback is enabled, the request is saved in `App_Data/leave-requests.jsonl`.
4. The app sends an email to HR.
5. The app calls the external public holiday API and reports any holidays inside the leave range.
6. HR logs in to the dashboard and can search by Employee ID to review leave history summary.
7. HR accepts or rejects the request.
8. The app sends an acceptance or rejection email to the employee.
9. HR decisions are stored in `App_Data/leave-decisions.jsonl`.

## HR Dashboard Login

Development credentials are configured in `appsettings.Development.json`.

```text
Username: hr123
Password: 12345678
```

Dashboard URL:

```text
/HrDashboard
```

## Project Structure

- `Models/LeaveRequest.cs` - employee leave request form model.
- `Models/LeaveRequestRecord.cs` - stored leave request with employee details and HR decision status.
- `Models/HrDashboardViewModel.cs` - HR dashboard summary and request list model.
- `Controllers/HomeController.cs` - employee form page.
- `Controllers/LeaveApiController.cs` - submit request API logic.
- `Controllers/HrDashboardController.cs` - HR login, dashboard, accept, reject, and logout.
- `Services/EmailService.cs` - SMTP email sending with MailKit.
- `Services/LeaveRequestStore.cs` - local JSONL request and decision storage.
- `Services/PublicHolidayService.cs` - external HTTP API caller for public holiday lookup.
- `Views/Home/Index.cshtml` - employee form UI.
- `Views/HrDashboard/Login.cshtml` - HR login page.
- `Views/HrDashboard/Index.cshtml` - HR dashboard UI.
- `sql/CreateTables.sql` - SQL Server database/table setup script.
- `App_Data/leave-requests.jsonl` - local fallback request storage.
- `App_Data/leave-decisions.jsonl` - local HR decision storage.

## Requirements

- .NET 8 SDK
- SQL Server Express named instance `SQLEXPRESS` for real SQL storage
- Gmail App Password or another SMTP provider credential

Check .NET:

```bash
dotnet --version
```

Expected version: `8.x`

## Configuration

Main settings live in `appsettings.json`.

```json
{
  "ConnectionStrings": {
    "EmployeeLeaveDB": "Server=localhost\\SQLEXPRESS;Database=EmployeeLeaveDB;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "HrEmail": "hr@example.com",
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "your-email@gmail.com",
    "Password": "your-gmail-app-password",
    "FromAddress": "your-email@gmail.com",
    "FromName": "EmployeeLeaveSystem"
  },
  "ExternalApis": {
    "PublicHolidays": {
      "Enabled": true,
      "BaseUrl": "https://date.nager.at/",
      "CountryCode": "IN",
      "TimeoutSeconds": 10
    }
  }
}
```

The public holiday API endpoint used by the app is:

```text
GET https://date.nager.at/api/v3/PublicHolidays/{Year}/{CountryCode}
```

Development-only settings live in `appsettings.Development.json`.

```json
{
  "LeaveRequests": {
    "UseSqlServer": false,
    "UseLocalFileFallbackWhenSqlUnavailable": true
  },
  "HrDashboard": {
    "Username": "hr123",
    "Password": "12345678"
  },
  "Smtp": {
    "DisableSending": false,
    "ContinueWhenEmailFails": true
  }
}
```

When `UseSqlServer` is `false`, leave requests are saved in:

```text
App_Data/leave-requests.jsonl
```

Set `UseSqlServer` to `true` after installing SQL Server Express or updating the connection string to a reachable SQL Server instance.

## SQL Server Setup

Install SQL Server Express with the named instance:

```text
SQLEXPRESS
```

The Windows service should appear as:

```text
SQL Server (SQLEXPRESS)
```

Service name:

```text
MSSQL$SQLEXPRESS
```

Start it from an administrator terminal:

```powershell
Start-Service 'MSSQL$SQLEXPRESS'
```

Create the database/table manually if needed:

```text
sql/CreateTables.sql
```

The app also attempts to create `EmployeeLeaveDB` and `dbo.LeaveRequests` automatically when SQL Server is reachable.

## Run The App

Restore/build/run:

```bash
dotnet restore
dotnet build
dotnet run
```

Open the URL shown in the terminal. The launch profile normally uses:

```text
http://localhost:5041
```

## Test The Employee Flow

1. Open `/`.
2. Enter employee ID, role, department, name, email, phone, from date, to date, and reason.
3. Submit the form.
4. Confirm the success message.
5. Check HR email inbox for the new leave request.

If SQL Server is unavailable and fallback is enabled, the request is saved in:

```text
App_Data/leave-requests.jsonl
```

## Test The HR Flow

1. Open `/HrDashboard`.
2. Log in with `hr123` / `12345678`.
3. Search by Employee ID or review employee summary totals and pending requests.
4. Click `Accept` or `Reject`.
5. Confirm the employee receives an acceptance or rejection email.
6. Confirm the dashboard status changes to `Accepted` or `Rejected`.

Decisions are stored locally in:

```text
App_Data/leave-decisions.jsonl
```

Use the dashboard `Reset Data` button to clear local leave requests and HR decisions.

## Email Notes

For Gmail SMTP:

- Use `smtp.gmail.com`
- Use port `587`
- Use a Gmail App Password, not the normal account password
- Make sure the machine running the app can connect to Gmail SMTP

If email fails, check:

- SMTP host and port
- Username and app password
- Network/firewall access to SMTP
- Target email spam folder

## Concepts Used

- ASP.NET Core MVC
- Razor views
- Web API-style controller action
- External HTTP API calling with `HttpClient`
- SQL Server with ADO.NET
- Local JSONL fallback storage
- MailKit SMTP
- Session-based HR dashboard login
- Anti-forgery protected forms
