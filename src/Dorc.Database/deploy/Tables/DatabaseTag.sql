CREATE TABLE [deploy].[DatabaseTag] (
    [DatabaseId] INT            NOT NULL,
    -- 400 rather than something tighter: almost every legacy value is a SINGLE
    -- tag, and dbo.DATABASE.DB_Type allowed 250 characters, so a narrower column
    -- would have silently dropped real tags on migration. 800 bytes also keeps
    -- both the clustered key and IX_DatabaseTag_Tag under the 900-byte limit.
    [Tag]        NVARCHAR (400) NOT NULL,
    CONSTRAINT [PK_DatabaseTag] PRIMARY KEY CLUSTERED ([DatabaseId] ASC, [Tag] ASC),
    CONSTRAINT [FK_DatabaseTag_Database] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[Database] ([Id]) ON DELETE CASCADE,
    -- Tag-first lookups ("which databases carry Endur?") are the common direction and
    -- are a seek on this index. The delimited column this replaces could only ever be
    -- matched with LIKE '%;tag;%', whose leading wildcard forces a scan.
    INDEX [IX_DatabaseTag_Tag] NONCLUSTERED ([Tag] ASC) INCLUDE ([DatabaseId])
);
