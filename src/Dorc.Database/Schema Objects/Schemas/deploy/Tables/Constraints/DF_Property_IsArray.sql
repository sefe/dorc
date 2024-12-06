ALTER TABLE [deploy].[Property]
    ADD CONSTRAINT [DF_Property_IsArray] DEFAULT ((0)) FOR [IsArray];

