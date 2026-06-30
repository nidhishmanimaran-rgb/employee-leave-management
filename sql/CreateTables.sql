-- SQL script to create the required database and table
-- Database name: EmployeeLeaveDB

-- NOTE:
-- 1) Run this in SQL Server Management Studio (SSMS)
-- 2) Update login/permissions as needed

IF DB_ID('EmployeeLeaveDB') IS NULL
BEGIN
    CREATE DATABASE EmployeeLeaveDB;
END
GO

USE EmployeeLeaveDB;
GO

IF OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId NVARCHAR(50) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(100) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
        PasswordHash NVARCHAR(128) NOT NULL,
        Role NVARCHAR(30) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL
    );
END
GO

IF OBJECT_ID('dbo.Employee', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Employee
    (
        EmployeeId NVARCHAR(100) NOT NULL CONSTRAINT PK_Employee PRIMARY KEY,
        UserId NVARCHAR(50) NOT NULL,
        Department NVARCHAR(100) NOT NULL,
        Designation NVARCHAR(100) NOT NULL,
        ManagerId NVARCHAR(50) NULL,
        LeaveBalance INT NOT NULL CONSTRAINT DF_Employee_LeaveBalance DEFAULT(12),
        CONSTRAINT FK_Employee_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
    );
END
GO

IF OBJECT_ID('dbo.LeaveRequests', 'U') IS NULL
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
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'EmployeeId') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD EmployeeId NVARCHAR(100) NOT NULL
        CONSTRAINT DF_LeaveRequests_EmployeeId DEFAULT('');
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'EmployeeId') IS NOT NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ALTER COLUMN EmployeeId NVARCHAR(100) NOT NULL;
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'EmployeeRole') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD EmployeeRole NVARCHAR(100) NOT NULL
        CONSTRAINT DF_LeaveRequests_EmployeeRole DEFAULT('');
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'EmployeeDepartment') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD EmployeeDepartment NVARCHAR(100) NOT NULL
        CONSTRAINT DF_LeaveRequests_EmployeeDepartment DEFAULT('');
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'EmployeePhone') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD EmployeePhone NVARCHAR(20) NULL;
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'Status') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD Status NVARCHAR(20) NOT NULL
        CONSTRAINT DF_LeaveRequests_Status DEFAULT(N'Pending');
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'SubmittedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD SubmittedAtUtc DATETIMEOFFSET NOT NULL
        CONSTRAINT DF_LeaveRequests_SubmittedAtUtc DEFAULT(SYSUTCDATETIME());
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'DecidedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD DecidedAtUtc DATETIMEOFFSET NULL;
END
GO

IF COL_LENGTH('dbo.LeaveRequests', 'DecisionComment') IS NULL
BEGIN
    ALTER TABLE dbo.LeaveRequests
    ADD DecisionComment NVARCHAR(500) NULL;
END
GO

-- Optional: quick check
-- SELECT TOP 10 * FROM dbo.LeaveRequests ORDER BY Id DESC;

