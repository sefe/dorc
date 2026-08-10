CREATE TABLE [deploy].[DatabaseTag] (
    [DatabaseId] INT            NOT NULL,
    [Tag]        NVARCHAR (200) NOT NULL,
    CONSTRAINT [PK_DatabaseTag] PRIMARY KEY CLUSTERED ([DatabaseId] ASC, [Tag] ASC),
    CONSTRAINT [FK_DatabaseTag_Database] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[Database] ([Id]) ON DELETE CASCADE,
    -- Tag-first lookups ("which databases carry Endur?") are the common direction and
    -- are a seek on this index. The delimited column this replaces could only ever be
    -- matched with LIKE '%;tag;%', whose leading wildcard forces a scan.
    INDEX [IX_DatabaseTag_Tag] NONCLUSTERED ([Tag] ASC) INCLUDE ([DatabaseId])
);
