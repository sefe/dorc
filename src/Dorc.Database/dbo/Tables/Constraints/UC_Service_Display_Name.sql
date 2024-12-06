ALTER TABLE [dbo].[Service]
    ADD CONSTRAINT UC_Service_Display_Name UNIQUE ([Display_Name]);
