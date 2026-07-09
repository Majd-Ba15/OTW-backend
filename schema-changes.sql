-- Apply manually to existing OTW databases because Program.cs uses EnsureCreated(), not Migrate().

IF OBJECT_ID('dbo.Waitlist', 'U') IS NOT NULL
    DROP TABLE dbo.Waitlist;

IF COL_LENGTH('dbo.RideRequests', 'FromLat') IS NULL
    ALTER TABLE dbo.RideRequests ADD FromLat decimal(10,7) NULL;

IF COL_LENGTH('dbo.RideRequests', 'FromLng') IS NULL
    ALTER TABLE dbo.RideRequests ADD FromLng decimal(10,7) NULL;

IF COL_LENGTH('dbo.RideRequests', 'ToLat') IS NULL
    ALTER TABLE dbo.RideRequests ADD ToLat decimal(10,7) NULL;

IF COL_LENGTH('dbo.RideRequests', 'ToLng') IS NULL
    ALTER TABLE dbo.RideRequests ADD ToLng decimal(10,7) NULL;