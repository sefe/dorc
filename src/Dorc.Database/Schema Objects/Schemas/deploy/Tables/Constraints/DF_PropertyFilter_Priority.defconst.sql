ALTER TABLE [deploy].[PropertyFilter]
    ADD CONSTRAINT [DF_PropertyFilter_Priority] DEFAULT ((0)) FOR [Priority];

