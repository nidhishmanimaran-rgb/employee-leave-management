# TODO - Employee Leave Management System (workflow + persistence)

## Plan checkpoints
- [ ] Add SQL-backed leave request storage and ensure primary workflow uses SQL only.
- [ ] Remove/stop using JSONL/local-file persistence as primary source for dashboards.
- [ ] Fix transaction flow order (DB -> email -> dashboards/messages).
- [ ] Public holiday API: log exact exception + show warning text when unavailable.
- [ ] Error handling: replace generic UI messages with detailed server logs (stack traces).
- [ ] Ensure leave submissions appear immediately on Employee and HR dashboards.
- [ ] Audit and update endpoint routes if mismatched with frontend expectations.
- [ ] Local storage audit for localStorage/sessionStorage/IndexedDB usage.

## Verification checklist
- [ ] Insert into dbo.LeaveRequests on successful submission.
- [ ] Leave request appears on EmployeeDashboard immediately after submit.
- [ ] Leave request appears as Pending on HrDashboard immediately after submit.
- [ ] Public holiday API failure does not fail the request; UI warning matches required text.
- [ ] Email failure does not roll back request; UI message matches required text.

