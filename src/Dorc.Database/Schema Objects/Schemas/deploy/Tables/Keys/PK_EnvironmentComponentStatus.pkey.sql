﻿ALTER TABLE [deploy].[EnvironmentComponentStatus]
    ADD CONSTRAINT [PK_EnvironmentComponentStatus] PRIMARY KEY CLUSTERED ([Id] ASC) WITH (ALLOW_PAGE_LOCKS = ON, ALLOW_ROW_LOCKS = ON, PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF);

