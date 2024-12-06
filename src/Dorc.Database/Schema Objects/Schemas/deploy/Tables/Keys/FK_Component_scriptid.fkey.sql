ALTER TABLE [deploy].[Component]
    ADD CONSTRAINT [FK_Component_scriptid] FOREIGN KEY ([ScriptId]) REFERENCES [deploy].[Script] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

