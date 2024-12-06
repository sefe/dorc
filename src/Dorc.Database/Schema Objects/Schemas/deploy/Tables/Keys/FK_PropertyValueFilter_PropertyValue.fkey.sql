ALTER TABLE [deploy].[PropertyValueFilter]
    ADD CONSTRAINT [FK_PropertyValueFilter_PropertyValue] FOREIGN KEY ([PropertyValueId]) REFERENCES [deploy].[PropertyValue] ([Id]) ON DELETE CASCADE ON UPDATE CASCADE;



