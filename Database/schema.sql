-- =============================================================
-- SQL Server (T-SQL) schema converted from MySQL/MariaDB dump
-- Source database: u800766782_one_connector
-- =============================================================
-- Conversion notes:
--   * int(11)            -> INT
--   * tinyint(4)         -> SMALLINT   (MySQL TINYINT is signed; SQL Server's is 0-255)
--   * tinyint(1)         -> BIT
--   * datetime(6)        -> DATETIME2(6)
--   * current_timestamp  -> SYSUTCDATETIME()  (dump used time_zone +00:00)
--   * varchar / text     -> NVARCHAR / NVARCHAR(MAX)  (utf8mb4 -> Unicode)
--   * char(36)           -> CHAR(36)  (UUIDs kept as text, same behavior as MySQL)
--   * AUTO_INCREMENT     -> IDENTITY(1,1)
--   * JSON + json_valid  -> NVARCHAR(MAX) + CHECK (ISJSON(...) = 1)
--   * Reserved words (user, key, file, ...) are bracketed
-- =============================================================

-- CREATE DATABASE [one_connector];
-- GO
-- USE [one_connector];
-- GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- -------------------------------------------------------------
-- mapping_table
-- -------------------------------------------------------------
CREATE TABLE [dbo].[mapping_table] (
    [id]           INT IDENTITY(1,1) NOT NULL,
    [entity_name]  NVARCHAR(256) NOT NULL,
    [cabinet_name] NVARCHAR(256) NOT NULL,
    [entity_code]  NVARCHAR(64)  NOT NULL,
    [entity_type]  NVARCHAR(32)  NOT NULL,
    [cabinet_code] NVARCHAR(64)  NOT NULL,
    [cabinet_type] NVARCHAR(32)  NOT NULL,
    [code]         NVARCHAR(64)  NOT NULL,
    [description]  NVARCHAR(512) NULL,
    [created_by]   CHAR(36)      NOT NULL CONSTRAINT [df_mapping_table_created_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [created_at]   DATETIME2(6)  NOT NULL CONSTRAINT [df_mapping_table_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at]   DATETIME2(6)  NOT NULL CONSTRAINT [df_mapping_table_updated_at] DEFAULT SYSUTCDATETIME(),
    [updated_by]   CHAR(36)      NOT NULL CONSTRAINT [df_mapping_table_updated_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    CONSTRAINT [pk_mapping_table] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_oc_mapping_table_code] UNIQUE ([code]),
    CONSTRAINT [uq_oc_mapping_table_entity_cabinet] UNIQUE ([entity_code], [entity_type], [entity_name], [cabinet_code], [cabinet_type], [cabinet_name])
);

-- -------------------------------------------------------------
-- mapping_field
-- -------------------------------------------------------------
CREATE TABLE [dbo].[mapping_field] (
    [id]                 INT IDENTITY(1,1) NOT NULL,
    [id_mapping_table]   INT           NOT NULL,
    [entity_field_name]  NVARCHAR(256) NOT NULL,
    [cabinet_field_name] NVARCHAR(256) NOT NULL,
    [entity_type_name]   NVARCHAR(128) NULL,
    [cabinet_type_name]  NVARCHAR(128) NULL,
    [entity_type_long]   INT           NULL,
    [cabinet_type_long]  INT           NULL,
    [created_by]         CHAR(36)      NOT NULL CONSTRAINT [df_mapping_field_created_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [created_at]         DATETIME2(6)  NOT NULL CONSTRAINT [df_mapping_field_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at]         DATETIME2(6)  NOT NULL CONSTRAINT [df_mapping_field_updated_at] DEFAULT SYSUTCDATETIME(),
    [updated_by]         CHAR(36)      NOT NULL CONSTRAINT [df_mapping_field_updated_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    CONSTRAINT [pk_mapping_field] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_oc_mapping_field_entity_field] UNIQUE ([id_mapping_table], [entity_field_name])
);

-- -------------------------------------------------------------
-- setting
-- -------------------------------------------------------------
CREATE TABLE [dbo].[setting] (
    [type]        NVARCHAR(32)   NOT NULL,
    [code]        NVARCHAR(64)   NOT NULL,
    [description] NVARCHAR(512)  NULL,
    [key]         NVARCHAR(64)   NOT NULL,
    [value]       NVARCHAR(2048) NULL,
    [created_by]  CHAR(36)       NOT NULL CONSTRAINT [df_setting_created_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [created_at]  DATETIME2(6)   NOT NULL CONSTRAINT [df_setting_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at]  DATETIME2(6)   NOT NULL CONSTRAINT [df_setting_updated_at] DEFAULT SYSUTCDATETIME(),
    [updated_by]  CHAR(36)       NOT NULL CONSTRAINT [df_setting_updated_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [configured]  BIT            NOT NULL CONSTRAINT [df_setting_configured] DEFAULT 0,
    [status]      BIT            NOT NULL CONSTRAINT [df_setting_status] DEFAULT 0,
    [required]    BIT            NOT NULL CONSTRAINT [df_setting_required] DEFAULT 1,
    CONSTRAINT [pk_setting] PRIMARY KEY CLUSTERED ([type], [code], [key])
);

-- -------------------------------------------------------------
-- synchronization
-- -------------------------------------------------------------
CREATE TABLE [dbo].[synchronization] (
    [id]               INT IDENTITY(1,1) NOT NULL,
    [direction]        NVARCHAR(16)  NOT NULL,
    [source]           NVARCHAR(256) NOT NULL,
    [destination]      NVARCHAR(256) NOT NULL,
    [created_by]       CHAR(36)      NOT NULL CONSTRAINT [df_synchronization_created_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [created_at]       DATETIME2(6)  NOT NULL CONSTRAINT [df_synchronization_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at]       DATETIME2(6)  NOT NULL CONSTRAINT [df_synchronization_updated_at] DEFAULT SYSUTCDATETIME(),
    [updated_by]       CHAR(36)      NOT NULL CONSTRAINT [df_synchronization_updated_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [id_mapping_table] INT           NOT NULL,
    [code]             NVARCHAR(64)  NOT NULL,
    [description]      NVARCHAR(512) NULL,
    [status]           NVARCHAR(16)  NULL,
    [max_retries]      INT           NOT NULL CONSTRAINT [df_synchronization_max_retries] DEFAULT 3,
    [timeout_seconds]  INT           NOT NULL CONSTRAINT [df_synchronization_timeout_seconds] DEFAULT 300,
    [next_run_at]      DATETIME2(6)  NULL,
    [retry_count]      INT           NOT NULL CONSTRAINT [df_synchronization_retry_count] DEFAULT 0,
    CONSTRAINT [pk_synchronization] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_synchronization_code] UNIQUE ([code])
);

-- -------------------------------------------------------------
-- synchronization_filter
-- -------------------------------------------------------------
CREATE TABLE [dbo].[synchronization_filter] (
    [id]                 INT IDENTITY(1,1) NOT NULL,
    [synchronization_id] INT           NOT NULL,
    [field_name]         NVARCHAR(128) NOT NULL,
    [operator]           NVARCHAR(16)  NOT NULL,
    [value]              NVARCHAR(512) NULL,
    [logical_operator]   NVARCHAR(8)   NOT NULL CONSTRAINT [df_sync_filter_logical_operator] DEFAULT 'AND',
    [sort_order]         INT           NOT NULL CONSTRAINT [df_sync_filter_sort_order] DEFAULT 0,
    [created_by]         CHAR(36)      NOT NULL CONSTRAINT [df_sync_filter_created_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    [created_at]         DATETIME2(6)  NOT NULL CONSTRAINT [df_sync_filter_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at]         DATETIME2(6)  NOT NULL CONSTRAINT [df_sync_filter_updated_at] DEFAULT SYSUTCDATETIME(),
    [updated_by]         CHAR(36)      NOT NULL CONSTRAINT [df_sync_filter_updated_by] DEFAULT '00000000-0000-0000-0000-000000000000',
    CONSTRAINT [pk_synchronization_filter] PRIMARY KEY CLUSTERED ([id])
);

-- -------------------------------------------------------------
-- logs
-- -------------------------------------------------------------
CREATE TABLE [dbo].[logs] (
    [id]                 CHAR(36)      NOT NULL,
    [id_synchronization] INT           NULL,
    [status]             SMALLINT      NOT NULL,
    [created_at]         DATETIME2(6)  NOT NULL,
    [updated_at]         DATETIME2(6)  NOT NULL,
    [last_attempt_at]    DATETIME2(6)  NULL,
    [last_success_at]    DATETIME2(6)  NULL,
    [retry_count]        INT           NOT NULL CONSTRAINT [df_logs_retry_count] DEFAULT 0,
    [error_message]      NVARCHAR(MAX) NULL,
    [file]               NVARCHAR(MAX) NULL,
    CONSTRAINT [pk_logs] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [ck_logs_file_json] CHECK ([file] IS NULL OR ISJSON([file]) = 1)
);

-- -------------------------------------------------------------
-- user
-- -------------------------------------------------------------
CREATE TABLE [dbo].[user] (
    [id]            CHAR(36)      NOT NULL,
    [username]      NVARCHAR(128) NOT NULL,
    [password_hash] NVARCHAR(512) NOT NULL,
    [display_name]  NVARCHAR(256) NULL,
    [email]         NVARCHAR(256) NULL,
    [is_active]     BIT           NOT NULL CONSTRAINT [df_user_is_active] DEFAULT 1,
    [created_at]    DATETIME2(6)  NOT NULL,
    [updated_at]    DATETIME2(6)  NOT NULL,
    [last_login_at] DATETIME2(6)  NULL,
    CONSTRAINT [pk_user] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_user_username] UNIQUE ([username])
);

-- -------------------------------------------------------------
-- Secondary indexes
-- -------------------------------------------------------------
CREATE NONCLUSTERED INDEX [ix_logs_updated]          ON [dbo].[logs] ([updated_at]);
CREATE NONCLUSTERED INDEX [ix_logs_status]           ON [dbo].[logs] ([status], [updated_at]);
CREATE NONCLUSTERED INDEX [ix_logs_synchronization]  ON [dbo].[logs] ([id_synchronization]);

CREATE NONCLUSTERED INDEX [fk_synchronization_mapping]      ON [dbo].[synchronization] ([id_mapping_table]);
CREATE NONCLUSTERED INDEX [ix_synchronization_due]          ON [dbo].[synchronization] ([next_run_at]);

CREATE NONCLUSTERED INDEX [ix_sync_filter_synchronization]  ON [dbo].[synchronization_filter] ([synchronization_id]);

-- -------------------------------------------------------------
-- Foreign keys
-- -------------------------------------------------------------
ALTER TABLE [dbo].[logs]
    ADD CONSTRAINT [fk_logs_synchronization]
    FOREIGN KEY ([id_synchronization]) REFERENCES [dbo].[synchronization] ([id])
    ON DELETE SET NULL ON UPDATE CASCADE;

ALTER TABLE [dbo].[mapping_field]
    ADD CONSTRAINT [fk_mapping_field_table]
    FOREIGN KEY ([id_mapping_table]) REFERENCES [dbo].[mapping_table] ([id])
    ON DELETE CASCADE;

ALTER TABLE [dbo].[synchronization]
    ADD CONSTRAINT [fk_synchronization_mapping]
    FOREIGN KEY ([id_mapping_table]) REFERENCES [dbo].[mapping_table] ([id]);

ALTER TABLE [dbo].[synchronization_filter]
    ADD CONSTRAINT [fk_sync_filter_synchronization]
    FOREIGN KEY ([synchronization_id]) REFERENCES [dbo].[synchronization] ([id])
    ON DELETE CASCADE;

COMMIT TRANSACTION;