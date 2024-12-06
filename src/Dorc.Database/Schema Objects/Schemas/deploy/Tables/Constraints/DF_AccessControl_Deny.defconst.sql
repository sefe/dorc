ALTER TABLE [deploy].[AccessControl]
    ADD CONSTRAINT [DF_AccessControl_Deny] DEFAULT ((0)) FOR [Deny];

