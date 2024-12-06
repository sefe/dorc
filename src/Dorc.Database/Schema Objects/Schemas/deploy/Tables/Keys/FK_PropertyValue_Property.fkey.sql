ALTER TABLE [deploy].[PropertyValue]
    ADD CONSTRAINT [FK_PropertyValue_Property] FOREIGN KEY ([PropertyId]) REFERENCES [deploy].[Property] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

