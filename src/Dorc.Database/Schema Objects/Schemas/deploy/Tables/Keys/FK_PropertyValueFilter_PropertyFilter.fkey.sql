ALTER TABLE [deploy].[PropertyValueFilter]
    ADD CONSTRAINT [FK_PropertyValueFilter_PropertyFilter] FOREIGN KEY ([PropertyFilterId]) REFERENCES [deploy].[PropertyFilter] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

