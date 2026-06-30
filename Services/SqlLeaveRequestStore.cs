using System.Data;
using EmployeeLeaveManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace EmployeeLeaveManagementSystem.Services;

public class SqlLeaveRequestStore
{
    private readonly string _connStr;
    private readonly LeaveRequestStore _fallbackStore;
    private readonly bool _allowFallback;
    private readonly ILogger<SqlLeaveRequestStore> _logger;

    public SqlLeaveRequestStore(
        IConfiguration config,
        LeaveRequestStore fallbackStore,
        ILogger<SqlLeaveRequestStore> logger)
    {
        var configuredConnStr = config.GetConnectionString("EmployeeLeaveDB") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configuredConnStr))
        {
            var builder = new SqlConnectionStringBuilder(configuredConnStr)
            {
                ConnectTimeout = 3
            };
            _connStr = builder.ConnectionString;
        }
        else
        {
            _connStr = string.Empty;
        }
        _fallbackStore = fallbackStore;
        _allowFallback = config.GetValue<bool>("LeaveRequests:UseLocalFileFallbackWhenSqlUnavailable", true);
        _logger = logger;
    }

    public async Task<LeaveRequestRecord> InsertRequestAsync(
        LeaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_allowFallback && string.IsNullOrWhiteSpace(_connStr))
        {
            _logger.LogWarning("SQL connection string is missing. Saving leave request to local backup storage.");
            return await _fallbackStore.SaveRequestAsync(request, cancellationToken);
        }

        try
        {
            await EnsureDatabaseAndTableAsync(cancellationToken);

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                await using var cmd = new SqlCommand(
                    @"INSERT INTO dbo.LeaveRequests
                          (EmployeeId, EmployeeRole, EmployeeDepartment, EmployeePhone, EmployeeName, EmployeeEmail,
                           FromDate, ToDate, Reason, Status, SubmittedAtUtc)
                      OUTPUT INSERTED.Id, INSERTED.SubmittedAtUtc
                      VALUES
                          (@EmployeeId, @EmployeeRole, @EmployeeDepartment, @EmployeePhone, @EmployeeName, @EmployeeEmail,
                           @FromDate, @ToDate, @Reason, N'Pending', SYSUTCDATETIME());",
                    conn,
                    transaction);

                AddRequestParameters(cmd, request);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("SQL insert did not return the inserted leave request.");

                var id = reader.GetInt32(0);
                var submittedAtUtc = reader.GetDateTimeOffset(1);
                await reader.DisposeAsync();

                await transaction.CommitAsync(cancellationToken);

                return new LeaveRequestRecord
                {
                    RequestId = id.ToString(),
                    SubmittedAtUtc = submittedAtUtc,
                    EmployeeId = request.EmployeeId,
                    EmployeeRole = request.EmployeeRole,
                    EmployeeDepartment = request.EmployeeDepartment,
                    EmployeePhone = request.EmployeePhone,
                    EmployeeName = request.EmployeeName,
                    EmployeeEmail = request.EmployeeEmail,
                    FromDate = request.FromDate,
                    ToDate = request.ToDate,
                    Reason = request.Reason,
                    Status = "Pending"
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (SqlException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL insert failed; saving leave request to local backup storage.");
            return await _fallbackStore.SaveRequestAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL insert could not proceed; saving leave request to local backup storage.");
            return await _fallbackStore.SaveRequestAsync(request, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<LeaveRequestRecord>> GetRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_allowFallback && string.IsNullOrWhiteSpace(_connStr))
            {
                _logger.LogWarning("SQL connection string is missing. Reading leave requests from local backup storage.");
                return await _fallbackStore.GetRequestsAsync(cancellationToken);
            }

            await EnsureDatabaseAndTableAsync(cancellationToken);

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(
                @"SELECT Id, EmployeeId, EmployeeRole, EmployeeDepartment, EmployeePhone, EmployeeName, EmployeeEmail,
                         FromDate, ToDate, Reason, Status, SubmittedAtUtc, DecidedAtUtc, DecisionComment
                  FROM dbo.LeaveRequests
                  ORDER BY SubmittedAtUtc DESC, Id DESC;",
                conn);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var list = new List<LeaveRequestRecord>();
            while (await reader.ReadAsync(cancellationToken))
                list.Add(ReadRecord(reader));

            return list;
        }
        catch (SqlException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL read failed; reading leave requests from local backup storage.");
            return await _fallbackStore.GetRequestsAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL read could not proceed; reading leave requests from local backup storage.");
            return await _fallbackStore.GetRequestsAsync(cancellationToken);
        }
    }

    public async Task<LeaveRequestRecord?> GetRequestAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_allowFallback && string.IsNullOrWhiteSpace(_connStr))
                return await _fallbackStore.GetRequestAsync(requestId, cancellationToken);

            if (!int.TryParse(requestId, out var id))
                return await _fallbackStore.GetRequestAsync(requestId, cancellationToken);

            await EnsureDatabaseAndTableAsync(cancellationToken);

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(
                @"SELECT TOP (1) Id, EmployeeId, EmployeeRole, EmployeeDepartment, EmployeePhone, EmployeeName, EmployeeEmail,
                         FromDate, ToDate, Reason, Status, SubmittedAtUtc, DecidedAtUtc, DecisionComment
                  FROM dbo.LeaveRequests
                  WHERE Id = @Id;",
                conn);

            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
        }
        catch (SqlException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL read failed while reading leave request {RequestId}; using local backup storage.", requestId);
            return await _fallbackStore.GetRequestAsync(requestId, cancellationToken);
        }
        catch (InvalidOperationException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL read could not proceed while reading leave request {RequestId}; using local backup storage.", requestId);
            return await _fallbackStore.GetRequestAsync(requestId, cancellationToken);
        }
    }

    public async Task SaveDecisionAsync(
        string requestId,
        string status,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_allowFallback && string.IsNullOrWhiteSpace(_connStr))
            {
                _logger.LogWarning("SQL connection string is missing. Saving leave decision to local backup storage.");
                await _fallbackStore.SaveDecisionAsync(requestId, status, comment, cancellationToken);
                return;
            }

            if (!int.TryParse(requestId, out var id))
                throw new InvalidOperationException("SQL leave request IDs must be numeric.");

            await EnsureDatabaseAndTableAsync(cancellationToken);

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(
                @"UPDATE dbo.LeaveRequests
                  SET Status = @Status,
                      DecisionComment = @DecisionComment,
                      DecidedAtUtc = SYSUTCDATETIME()
                  WHERE Id = @Id;",
                conn);

            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 20) { Value = status });
            cmd.Parameters.Add(new SqlParameter("@DecisionComment", SqlDbType.NVarChar, 500)
            {
                Value = string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim()
            });

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (rows == 0)
                throw new InvalidOperationException($"Leave request {requestId} was not found.");
        }
        catch (SqlException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL update failed for leave request {RequestId}; saving decision to local backup storage.", requestId);
            await _fallbackStore.SaveDecisionAsync(requestId, status, comment, cancellationToken);
        }
        catch (InvalidOperationException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL update could not proceed for leave request {RequestId}; saving decision to local backup storage.", requestId);
            await _fallbackStore.SaveDecisionAsync(requestId, status, comment, cancellationToken);
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_allowFallback && string.IsNullOrWhiteSpace(_connStr))
            {
                _logger.LogWarning("SQL connection string is missing. Resetting local backup storage instead.");
                await _fallbackStore.ResetAsync(cancellationToken);
                return;
            }

            await EnsureDatabaseAndTableAsync(cancellationToken);

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand("DELETE FROM dbo.LeaveRequests;", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL reset failed; resetting local backup storage instead.");
            await _fallbackStore.ResetAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (_allowFallback)
        {
            _logger.LogWarning(ex, "SQL reset could not proceed; resetting local backup storage instead.");
            await _fallbackStore.ResetAsync(cancellationToken);
        }
    }

    public async Task EnsureDatabaseAndTableAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(_connStr);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Connection string 'EmployeeLeaveDB' must include a Database value.");

        var masterBuilder = new SqlConnectionStringBuilder(_connStr)
        {
            InitialCatalog = "master"
        };

        await using (var masterConn = new SqlConnection(masterBuilder.ConnectionString))
        {
            await masterConn.OpenAsync(cancellationToken);

            await using var createDatabaseCmd = new SqlCommand(
                @"DECLARE @sql nvarchar(max);
                  IF DB_ID(@DatabaseName) IS NULL
                  BEGIN
                      SET @sql = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
                      EXEC(@sql);
                  END",
                masterConn);

            createDatabaseCmd.Parameters.Add(new SqlParameter("@DatabaseName", SqlDbType.NVarChar, 128)
            {
                Value = databaseName
            });

            await createDatabaseCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(cancellationToken);
        await EnsureTableAsync(conn, cancellationToken);
    }

    private static async Task EnsureTableAsync(SqlConnection conn, CancellationToken cancellationToken)
    {
        await using var createTableCmd = new SqlCommand(
            @"IF OBJECT_ID(N'dbo.LeaveRequests', N'U') IS NULL
              BEGIN
                  CREATE TABLE dbo.LeaveRequests
                  (
                      Id INT IDENTITY(1,1) PRIMARY KEY,
                      EmployeeId NVARCHAR(100) NOT NULL,
                      EmployeeRole NVARCHAR(100) NOT NULL,
                      EmployeeDepartment NVARCHAR(100) NOT NULL,
                      EmployeePhone NVARCHAR(20) NULL,
                      EmployeeName NVARCHAR(100) NOT NULL,
                      EmployeeEmail NVARCHAR(100) NOT NULL,
                      FromDate DATE NOT NULL,
                      ToDate DATE NOT NULL,
                      Reason NVARCHAR(500) NOT NULL,
                      Status NVARCHAR(20) NOT NULL CONSTRAINT DF_LeaveRequests_Status DEFAULT(N'Pending'),
                      SubmittedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_LeaveRequests_SubmittedAtUtc DEFAULT(SYSUTCDATETIME()),
                      DecidedAtUtc DATETIMEOFFSET NULL,
                      DecisionComment NVARCHAR(500) NULL
                  );
              END",
            conn);

        await createTableCmd.ExecuteNonQueryAsync(cancellationToken);

        await using var alterTableCmd = new SqlCommand(
            @"IF COL_LENGTH(N'dbo.LeaveRequests', N'EmployeeId') IS NOT NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests ALTER COLUMN EmployeeId NVARCHAR(100) NOT NULL;
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'EmployeeRole') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests
                  ADD EmployeeRole NVARCHAR(100) NOT NULL
                      CONSTRAINT DF_LeaveRequests_EmployeeRole DEFAULT(N'');
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'EmployeeDepartment') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests
                  ADD EmployeeDepartment NVARCHAR(100) NOT NULL
                      CONSTRAINT DF_LeaveRequests_EmployeeDepartment DEFAULT(N'');
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'EmployeePhone') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests ADD EmployeePhone NVARCHAR(20) NULL;
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'Status') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests
                  ADD Status NVARCHAR(20) NOT NULL
                      CONSTRAINT DF_LeaveRequests_Status DEFAULT(N'Pending');
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'SubmittedAtUtc') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests
                  ADD SubmittedAtUtc DATETIMEOFFSET NOT NULL
                      CONSTRAINT DF_LeaveRequests_SubmittedAtUtc DEFAULT(SYSUTCDATETIME());
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'DecidedAtUtc') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests ADD DecidedAtUtc DATETIMEOFFSET NULL;
              END

              IF COL_LENGTH(N'dbo.LeaveRequests', N'DecisionComment') IS NULL
              BEGIN
                  ALTER TABLE dbo.LeaveRequests ADD DecisionComment NVARCHAR(500) NULL;
              END",
            conn);

        await alterTableCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRequestParameters(SqlCommand cmd, LeaveRequest request)
    {
        cmd.Parameters.Add(new SqlParameter("@EmployeeId", SqlDbType.NVarChar, 100) { Value = request.EmployeeId });
        cmd.Parameters.Add(new SqlParameter("@EmployeeRole", SqlDbType.NVarChar, 100) { Value = request.EmployeeRole });
        cmd.Parameters.Add(new SqlParameter("@EmployeeDepartment", SqlDbType.NVarChar, 100) { Value = request.EmployeeDepartment });
        cmd.Parameters.Add(new SqlParameter("@EmployeePhone", SqlDbType.NVarChar, 20)
        {
            Value = string.IsNullOrWhiteSpace(request.EmployeePhone) ? DBNull.Value : request.EmployeePhone
        });
        cmd.Parameters.Add(new SqlParameter("@EmployeeName", SqlDbType.NVarChar, 100) { Value = request.EmployeeName });
        cmd.Parameters.Add(new SqlParameter("@EmployeeEmail", SqlDbType.NVarChar, 100) { Value = request.EmployeeEmail });
        cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = request.FromDate.ToDateTime(TimeOnly.MinValue) });
        cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = request.ToDate.ToDateTime(TimeOnly.MinValue) });
        cmd.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 500) { Value = request.Reason });
    }

    private static LeaveRequestRecord ReadRecord(SqlDataReader reader)
    {
        return new LeaveRequestRecord
        {
            RequestId = reader.GetInt32(reader.GetOrdinal("Id")).ToString(),
            SubmittedAtUtc = reader.GetDateTimeOffset(reader.GetOrdinal("SubmittedAtUtc")),
            EmployeeId = reader.GetString(reader.GetOrdinal("EmployeeId")),
            EmployeeRole = reader.GetString(reader.GetOrdinal("EmployeeRole")),
            EmployeeDepartment = reader.GetString(reader.GetOrdinal("EmployeeDepartment")),
            EmployeePhone = reader.IsDBNull(reader.GetOrdinal("EmployeePhone"))
                ? null
                : reader.GetString(reader.GetOrdinal("EmployeePhone")),
            EmployeeName = reader.GetString(reader.GetOrdinal("EmployeeName")),
            EmployeeEmail = reader.GetString(reader.GetOrdinal("EmployeeEmail")),
            FromDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("FromDate"))),
            ToDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("ToDate"))),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            DecidedAtUtc = reader.IsDBNull(reader.GetOrdinal("DecidedAtUtc"))
                ? null
                : reader.GetDateTimeOffset(reader.GetOrdinal("DecidedAtUtc")),
            DecisionComment = reader.IsDBNull(reader.GetOrdinal("DecisionComment"))
                ? null
                : reader.GetString(reader.GetOrdinal("DecisionComment"))
        };
    }
}
