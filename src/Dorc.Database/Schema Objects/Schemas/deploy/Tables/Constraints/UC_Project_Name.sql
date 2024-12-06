ALTER TABLE [deploy].[Project]
    ADD CONSTRAINT UC_Project_Name UNIQUE ([Name]);
