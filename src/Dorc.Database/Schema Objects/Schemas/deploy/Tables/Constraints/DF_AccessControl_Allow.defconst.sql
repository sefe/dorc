ALTER TABLE [deploy].[AccessControl]
    ADD CONSTRAINT [DF_AccessControl_Allow] DEFAULT ((0)) FOR [Allow];

