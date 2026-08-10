CREATE TABLE [deploy].[ServerTag] (
    [ServerId] INT            NOT NULL,
    -- See DatabaseTag.Tag. The legacy Application_Server_Name allowed 1000
    -- characters; anything beyond this column stops the deploy rather than
    -- being dropped, so an over-long tag is a decision, not a silent loss.
    [Tag]      NVARCHAR (400) NOT NULL,
    CONSTRAINT [PK_ServerTag] PRIMARY KEY CLUSTERED ([ServerId] ASC, [Tag] ASC),
    CONSTRAINT [FK_ServerTag_Server] FOREIGN KEY ([ServerId]) REFERENCES [deploy].[Server] ([Id]) ON DELETE CASCADE,
    INDEX [IX_ServerTag_Tag] NONCLUSTERED ([Tag] ASC) INCLUDE ([ServerId])
);
