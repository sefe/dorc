ALTER TABLE [dbo].[Service]
    ADD CONSTRAINT UC_Service_Service_Name UNIQUE ([Service_Name]);
