/*
    Project Nest sharing database.

    Creates the database ProjectNestSharing, the three sharing tables, and the
    stored procedures the sharing server calls. Run this once in SSMS (or sqlcmd)
    before starting ProjectNest.Server with a Sharing:ConnectionString.

    The desktop app's own projects.db is untouched. This database only holds
    Nest Eggs, share codes, and the activity log.

    Connection string example:
      Server=localhost;Database=ProjectNestSharing;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'ProjectNestSharing') IS NULL
    CREATE DATABASE ProjectNestSharing;
GO

USE ProjectNestSharing;
GO

IF OBJECT_ID(N'dbo.NestEggs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NestEggs
    (
        Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_NestEggs PRIMARY KEY,
        SchemaVersion    INT              NOT NULL,
        CreatedUtc       DATETIME2(3)     NOT NULL,
        ExpiresUtc       DATETIME2(3)     NOT NULL,
        SenderLabel      NVARCHAR(80)     NOT NULL,
        ProjectName      NVARCHAR(200)    NOT NULL,
        SourceProjectId  UNIQUEIDENTIFIER NOT NULL,
        PayloadJson      NVARCHAR(MAX)    NOT NULL,
        PayloadSha256    CHAR(64)         NOT NULL,
        ByteLength       INT              NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.Shares', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shares
    (
        Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Shares PRIMARY KEY,
        EggId        UNIQUEIDENTIFIER NOT NULL,
        Code         NVARCHAR(8)      NOT NULL,
        CreatedUtc   DATETIME2(3)     NOT NULL,
        ExpiresUtc   DATETIME2(3)     NOT NULL,
        RevokedUtc   DATETIME2(3)     NULL,
        FetchCount   INT              NOT NULL CONSTRAINT DF_Shares_FetchCount DEFAULT (0),
        ImportCount  INT              NOT NULL CONSTRAINT DF_Shares_ImportCount DEFAULT (0),
        CONSTRAINT FK_Shares_NestEggs FOREIGN KEY (EggId) REFERENCES dbo.NestEggs (Id),
        CONSTRAINT UQ_Shares_Code UNIQUE (Code)
    );
END
GO

IF OBJECT_ID(N'dbo.ShareEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShareEvents
    (
        Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ShareEvents PRIMARY KEY,
        ShareId       UNIQUEIDENTIFIER NOT NULL,
        EggId         UNIQUEIDENTIFIER NOT NULL,
        EventType     NVARCHAR(20)     NOT NULL,
        OccurredUtc   DATETIME2(3)     NOT NULL,
        MachineLabel  NVARCHAR(80)     NULL,
        Detail        NVARCHAR(500)    NULL,
        CONSTRAINT FK_ShareEvents_Shares FOREIGN KEY (ShareId) REFERENCES dbo.Shares (Id),
        CONSTRAINT FK_ShareEvents_NestEggs FOREIGN KEY (EggId) REFERENCES dbo.NestEggs (Id)
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ShareEvents_ShareId_OccurredUtc'
      AND object_id = OBJECT_ID(N'dbo.ShareEvents'))
BEGIN
    CREATE INDEX IX_ShareEvents_ShareId_OccurredUtc
        ON dbo.ShareEvents (ShareId, OccurredUtc);
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_Share_CodeExists
    Purpose:  Tell the server whether an 8-character code is already issued.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Share_CodeExists
    @Code   NVARCHAR(8),
    @Exists BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.Shares WHERE Code = @Code)
        SET @Exists = 1;
    ELSE
        SET @Exists = 0;

    RETURN 0;
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_Share_Create
    Purpose:  Store one Nest Egg, its share code, and the Created event together.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Share_Create
    @EggId            UNIQUEIDENTIFIER,
    @ShareId          UNIQUEIDENTIFIER,
    @EventId          UNIQUEIDENTIFIER,
    @SchemaVersion    INT,
    @CreatedUtc       DATETIME2(3),
    @ExpiresUtc       DATETIME2(3),
    @SenderLabel      NVARCHAR(80),
    @ProjectName      NVARCHAR(200),
    @SourceProjectId  UNIQUEIDENTIFIER,
    @PayloadJson      NVARCHAR(MAX),
    @PayloadSha256    CHAR(64),
    @ByteLength       INT,
    @Code             NVARCHAR(8)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.NestEggs
            (Id, SchemaVersion, CreatedUtc, ExpiresUtc, SenderLabel, ProjectName, SourceProjectId, PayloadJson, PayloadSha256, ByteLength)
        VALUES
            (@EggId, @SchemaVersion, @CreatedUtc, @ExpiresUtc, @SenderLabel, @ProjectName, @SourceProjectId, @PayloadJson, @PayloadSha256, @ByteLength);

        INSERT INTO dbo.Shares
            (Id, EggId, Code, CreatedUtc, ExpiresUtc, FetchCount, ImportCount)
        VALUES
            (@ShareId, @EggId, @Code, @CreatedUtc, @ExpiresUtc, 0, 0);

        INSERT INTO dbo.ShareEvents
            (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
        VALUES
            (@EventId, @ShareId, @EggId, N'Created', @CreatedUtc, @SenderLabel, @ProjectName);

        COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_Share_GetByCode
    Purpose:  Load one share and its egg for preview, fetch, import, and revoke.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Share_GetByCode
    @Code NVARCHAR(8)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.Id              AS ShareId,
        s.Code,
        s.EggId,
        s.RevokedUtc,
        s.ExpiresUtc,
        e.SchemaVersion,
        e.CreatedUtc,
        e.SenderLabel,
        e.ProjectName,
        e.SourceProjectId,
        e.PayloadJson,
        e.PayloadSha256
    FROM dbo.Shares AS s
    INNER JOIN dbo.NestEggs AS e ON e.Id = s.EggId
    WHERE s.Code = @Code;
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_ShareEvent_Insert
    Purpose:  Append one activity-log row (previewed, rejected, and similar).
*/
CREATE OR ALTER PROCEDURE dbo.usp_ShareEvent_Insert
    @EventId       UNIQUEIDENTIFIER,
    @ShareId       UNIQUEIDENTIFIER,
    @EggId         UNIQUEIDENTIFIER,
    @EventType     NVARCHAR(20),
    @OccurredUtc   DATETIME2(3),
    @MachineLabel  NVARCHAR(80) = NULL,
    @Detail        NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        INSERT INTO dbo.ShareEvents
            (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
        VALUES
            (@EventId, @ShareId, @EggId, @EventType, @OccurredUtc, @MachineLabel, @Detail);

        RETURN 0;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_Share_RecordFetch
    Purpose:  Count a download and append the Fetched event in one transaction.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Share_RecordFetch
    @ShareId       UNIQUEIDENTIFIER,
    @EggId         UNIQUEIDENTIFIER,
    @EventId       UNIQUEIDENTIFIER,
    @OccurredUtc   DATETIME2(3),
    @MachineLabel  NVARCHAR(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.Shares
        SET FetchCount = FetchCount + 1
        WHERE Id = @ShareId;

        INSERT INTO dbo.ShareEvents
            (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
        VALUES
            (@EventId, @ShareId, @EggId, N'Fetched', @OccurredUtc, @MachineLabel, NULL);

        COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_Share_RecordImport
    Purpose:  Count a reported import and append the Imported event in one transaction.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Share_RecordImport
    @ShareId       UNIQUEIDENTIFIER,
    @EggId         UNIQUEIDENTIFIER,
    @EventId       UNIQUEIDENTIFIER,
    @OccurredUtc   DATETIME2(3),
    @MachineLabel  NVARCHAR(80) = NULL,
    @Detail        NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.Shares
        SET ImportCount = ImportCount + 1
        WHERE Id = @ShareId;

        INSERT INTO dbo.ShareEvents
            (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
        VALUES
            (@EventId, @ShareId, @EggId, N'Imported', @OccurredUtc, @MachineLabel, @Detail);

        COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_Share_Revoke
    Purpose:  Mark a share revoked and append the Revoked event.
              Returns 1 when the share was already revoked.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Share_Revoke
    @ShareId       UNIQUEIDENTIFIER,
    @EggId         UNIQUEIDENTIFIER,
    @EventId       UNIQUEIDENTIFIER,
    @OccurredUtc   DATETIME2(3),
    @MachineLabel  NVARCHAR(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.Shares
        SET RevokedUtc = @OccurredUtc
        WHERE Id = @ShareId
          AND RevokedUtc IS NULL;

        IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK TRANSACTION;
            RETURN 1;
        END

        INSERT INTO dbo.ShareEvents
            (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
        VALUES
            (@EventId, @ShareId, @EggId, N'Revoked', @OccurredUtc, @MachineLabel, NULL);

        COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Procedure: dbo.usp_ShareEvent_List
    Purpose:  Activity log for one share, oldest first.
*/
CREATE OR ALTER PROCEDURE dbo.usp_ShareEvent_List
    @ShareId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        EventType,
        OccurredUtc,
        MachineLabel,
        Detail
    FROM dbo.ShareEvents
    WHERE ShareId = @ShareId
    ORDER BY OccurredUtc, Id;
END
GO
