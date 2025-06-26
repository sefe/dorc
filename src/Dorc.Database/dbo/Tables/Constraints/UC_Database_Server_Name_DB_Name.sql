ALTER TABLE [dbo].[DATABASE]
    ADD CONSTRAINT UC_Database_Server_Name_DB_Name UNIQUE ([Server_Name], [DB_Name]);