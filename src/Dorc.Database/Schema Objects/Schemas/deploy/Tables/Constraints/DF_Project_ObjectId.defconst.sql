ALTER TABLE [deploy].[Project]
    ADD CONSTRAINT [DF_Project_ObjectId] DEFAULT (newsequentialid()) FOR [ObjectId];

