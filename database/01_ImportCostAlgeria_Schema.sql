/* ============================================================================
 * PROJET : ImportCost Algeria — Logiciel Professionnel de Calcul du Coût de
 *          Revient des Marchandises Importées en Algérie
 * FICHIER : database/01_ImportCostAlgeria_Schema.sql
 * SGBD    : Microsoft SQL Server (T-SQL) / Compatible EF Core 8
 * OBJET   : Schéma relationnel complet (Multi-entreprise, Moteur Réglementaire
 *           Versionné, Devises/ALCES, Import Excel & Mapping, Frais & Incoterms,
 *           Moteur de Calcul, IA avec validation humaine, Audit immuable)
 * ============================================================================ */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================================
-- 1. SCHÉMAS LOGIQUES (Séparation modulaire en base de données)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'core')       EXEC('CREATE SCHEMA [core]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'reg')        EXEC('CREATE SCHEMA [reg]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'excel')      EXEC('CREATE SCHEMA [excel]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'calc')       EXEC('CREATE SCHEMA [calc]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'ai')         EXEC('CREATE SCHEMA [ai]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'audit')      EXEC('CREATE SCHEMA [audit]');
GO

-- ============================================================================
-- 2. MODULE CORE : ENTREPRISES (MULTI-TENANT), UTILISATEURS & RÉFÉRENTIELS
-- ============================================================================

CREATE TABLE [core].[Companies] (
    [Id]                        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [Code]                      NVARCHAR(32)     NOT NULL,
    [LegalName]                 NVARCHAR(250)    NOT NULL,
    [NIF]                       NVARCHAR(20)     NULL,     -- Numéro d'Identification Fiscale (Algérie)
    [NIS]                       NVARCHAR(20)     NULL,     -- Numéro d'Identification Statistique
    [RC]                        NVARCHAR(30)     NULL,     -- Registre du Commerce
    [AI]                        NVARCHAR(20)     NULL,     -- Article d'Imposition
    [Address]                   NVARCHAR(500)    NULL,
    [Wilaya]                    NVARCHAR(100)    NULL,
    [DefaultFunctionalCurrency] CHAR(3)          NOT NULL DEFAULT 'DZD',
    -- Paramètre métier (Section 24) : Par défaut 1 (TVA d'importation traitée comme non récupérable et intégrée au coût de revient)
    [IsImportVatNonRecoverable] BIT              NOT NULL DEFAULT 1,
    [RoundingDecimalsDzd]       TINYINT          NOT NULL DEFAULT 2,
    [IsActive]                  BIT              NOT NULL DEFAULT 1,
    [CreatedAtUtc]              DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc]              DATETIME2(7)     NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Companies_Code] UNIQUE ([Code])
);

CREATE TABLE [core].[Users] (
    [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CompanyId]    UNIQUEIDENTIFIER NOT NULL,
    [Username]     NVARCHAR(100)    NOT NULL,
    [Email]        NVARCHAR(255)    NOT NULL,
    [FullName]     NVARCHAR(200)    NOT NULL,
    [PasswordHash] NVARCHAR(512)    NOT NULL,
    -- Rôles (Section 33) : 'ADMINISTRATEUR', 'UTILISATEUR', 'CONSULTATION'
    [Role]         NVARCHAR(30)     NOT NULL,
    [IsActive]     BIT              NOT NULL DEFAULT 1,
    [CreatedAtUtc] DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Users_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies]([Id]),
    CONSTRAINT [CK_Users_Role] CHECK ([Role] IN ('ADMINISTRATEUR', 'UTILISATEUR', 'CONSULTATION')),
    CONSTRAINT [UQ_Users_Company_Email] UNIQUE ([CompanyId], [Email])
);

CREATE TABLE [core].[Countries] (
    [IsoAlpha2]    CHAR(2)       NOT NULL,
    [IsoAlpha3]    CHAR(3)       NOT NULL,
    [NumericCode]  CHAR(3)       NULL,
    [NameFr]       NVARCHAR(150) NOT NULL,
    [NameAr]       NVARCHAR(150) NULL,
    [CustomsZone]  NVARCHAR(50)  NULL, -- Ex: UE, GZALE, ZLECAf, DroitCommun
    [IsActive]     BIT           NOT NULL DEFAULT 1,
    CONSTRAINT [PK_Countries] PRIMARY KEY CLUSTERED ([IsoAlpha2]),
    CONSTRAINT [UQ_Countries_IsoAlpha3] UNIQUE ([IsoAlpha3])
);

CREATE TABLE [core].[Currencies] (
    [Code]          CHAR(3)       NOT NULL, -- EUR, USD, GBP, CNY, AED, TRY, DZD...
    [NumericCode]   CHAR(3)       NULL,
    [NameFr]        NVARCHAR(100) NOT NULL,
    [Symbol]        NVARCHAR(10)   NOT NULL,
    [QuotityUnit]   INT           NOT NULL DEFAULT 1, -- Ex: 1 EUR, ou 100 JPY
    [DecimalPlaces] TINYINT       NOT NULL DEFAULT 2,
    [IsActive]      BIT           NOT NULL DEFAULT 1,
    CONSTRAINT [PK_Currencies] PRIMARY KEY CLUSTERED ([Code])
);

-- Base de taux de change versionnée (Section 14 — ALCES / Banque d'Algérie)
CREATE TABLE [core].[ExchangeRates] (
    [Id]                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CurrencyCode]       CHAR(3)          NOT NULL,
    [TargetCurrencyCode] CHAR(3)          NOT NULL DEFAULT 'DZD',
    [RateToDzd]          DECIMAL(18, 6)   NOT NULL,
    [ValidFrom]          DATE             NOT NULL,
    [ValidTo]            DATE             NULL,
    -- Type : 'OFFICIEL_DOUANE_ALCES', 'BANQUE_ALGERIE', 'MANUEL_EXCEPTIONNEL'
    [RateType]           NVARCHAR(40)     NOT NULL,
    [SourceName]         NVARCHAR(200)    NOT NULL, -- Ex: 'Portail officiel ALCES - DGD Algérie'
    [SourceReference]    NVARCHAR(200)    NULL,
    [RetrievedAtUtc]     DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    [CreatedByUserId]    UNIQUEIDENTIFIER NULL,
    [IsActive]           BIT              NOT NULL DEFAULT 1,
    CONSTRAINT [PK_ExchangeRates] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ExchangeRates_Currency] FOREIGN KEY ([CurrencyCode]) REFERENCES [core].[Currencies]([Code]),
    CONSTRAINT [CK_ExchangeRates_Positive] CHECK ([RateToDzd] > 0),
    CONSTRAINT [CK_ExchangeRates_Dates] CHECK ([ValidTo] IS NULL OR [ValidTo] >= [ValidFrom])
);

CREATE INDEX [IX_ExchangeRates_Lookup]
    ON [core].[ExchangeRates] ([CurrencyCode], [RateType], [ValidFrom], [ValidTo])
    INCLUDE ([RateToDzd], [SourceName]);

-- Incoterms (Sections 7 & 8 : V1 EXW, FOB, CFR + Architecture prête pour FCA, FAS, CIF, CPT, CIP, DAP, DPU, DDP)
CREATE TABLE [core].[Incoterms] (
    [Code]                    NVARCHAR(10)  NOT NULL,
    [IncotermVersion]         NVARCHAR(20)  NOT NULL DEFAULT 'ICC_2020',
    [NameFr]                  NVARCHAR(150) NOT NULL,
    [IsSupportedInV1]         BIT           NOT NULL DEFAULT 0, -- 1 pour EXW, FOB, CFR
    [RequiresPreCarriage]     BIT           NOT NULL DEFAULT 0,
    [RequiresExportClearance] BIT           NOT NULL DEFAULT 0,
    [RequiresMainFreight]     BIT           NOT NULL DEFAULT 0,
    [RequiresInsurance]       BIT           NOT NULL DEFAULT 0,
    [FreightAlreadyInPrice]   BIT           NOT NULL DEFAULT 0,
    [InsuranceAlreadyInPrice] BIT           NOT NULL DEFAULT 0,
    [LegalNote]               NVARCHAR(500) NULL,
    CONSTRAINT [PK_Incoterms] PRIMARY KEY CLUSTERED ([Code])
);

-- Configuration dynamique des champs requis selon l'Incoterm (Section 8)
CREATE TABLE [core].[IncotermFieldRules] (
    [Id]                          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [IncotermCode]                NVARCHAR(10)     NOT NULL,
    [FeeCategoryCode]             NVARCHAR(50)     NOT NULL,
    [FieldLabelFr]                NVARCHAR(150)    NOT NULL,
    [IsRequiredForCustomsValue]   BIT              NOT NULL,
    [IsDisplayedByDefault]        BIT              NOT NULL DEFAULT 1,
    [CustomsAdjustmentType]       NVARCHAR(30)     NOT NULL, -- 'INCLUDED_IN_PRICE', 'ADD_TO_CUSTOMS_VALUE', 'EXCLUDE_FROM_CUSTOMS_VALUE'
    [AnomalySeverityIfMissing]    NVARCHAR(20)     NOT NULL DEFAULT 'AVERTISSEMENT', -- 'INFO', 'AVERTISSEMENT', 'ERREUR', 'BLOCAGE'
    [RegulatoryJustificationRef]  NVARCHAR(250)    NULL,
    [DisplayOrder]                INT              NOT NULL DEFAULT 1,
    CONSTRAINT [PK_IncotermFieldRules] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_IncotermFieldRules_Incoterms] FOREIGN KEY ([IncotermCode]) REFERENCES [core].[Incoterms]([Code])
);

-- Fournisseurs par entreprise
CREATE TABLE [core].[Suppliers] (
    [Id]                UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CompanyId]         UNIQUEIDENTIFIER NOT NULL,
    [Code]              NVARCHAR(50)     NOT NULL,
    [Name]              NVARCHAR(250)    NOT NULL,
    [CountryIso2]       CHAR(2)          NOT NULL,
    [DefaultCurrency]   CHAR(3)          NULL,
    [DefaultIncoterm]   NVARCHAR(10)     NULL,
    [Address]           NVARCHAR(500)    NULL,
    [CreatedAtUtc]      DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Suppliers] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Suppliers_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies]([Id]),
    CONSTRAINT [FK_Suppliers_Countries] FOREIGN KEY ([CountryIso2]) REFERENCES [core].[Countries]([IsoAlpha2]),
    CONSTRAINT [UQ_Suppliers_Company_Code] UNIQUE ([CompanyId], [Code])
);

-- Base Produits réutilisable par entreprise (Section 31)
CREATE TABLE [core].[Products] (
    [Id]                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CompanyId]               UNIQUEIDENTIFIER NOT NULL,
    [Reference]               NVARCHAR(100)    NOT NULL,
    [Designation]             NVARCHAR(500)    NOT NULL,
    [RegulatoryDesignation]   NVARCHAR(500)    NULL,
    [ConfirmedHsCode10]       NVARCHAR(14)     NULL, -- Code SH confirmé à 10 chiffres (ex: 8708.99.90.00)
    [HabitualOriginCountry]   CHAR(2)          NULL,
    [MeasurementUnit]         NVARCHAR(20)     NOT NULL DEFAULT 'U', -- UQN : U, KG, L, M, etc.
    [HabitualDutyRatePercent] DECIMAL(9, 4)    NULL, -- Purement indicatif : jamais appliqué sans vérification réglementaire à la date d'opération
    [HabitualVatRatePercent]  DECIMAL(9, 4)    NULL,
    [UnitGrossWeightKg]       DECIMAL(18, 4)   NULL, -- Facultatif en V1 (Section 12)
    [UnitNetWeightKg]         DECIMAL(18, 4)   NULL,
    [UnitVolumeM3]            DECIMAL(18, 6)   NULL, -- Facultatif en V1
    [Notes]                   NVARCHAR(MAX)    NULL,
    [IsActive]                BIT              NOT NULL DEFAULT 1,
    [CreatedAtUtc]            DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc]            DATETIME2(7)     NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Products_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies]([Id]),
    CONSTRAINT [FK_Products_OriginCountry] FOREIGN KEY ([HabitualOriginCountry]) REFERENCES [core].[Countries]([IsoAlpha2]),
    CONSTRAINT [UQ_Products_Company_Reference] UNIQUE ([CompanyId], [Reference])
);

-- ============================================================================
-- 3. MODULE REGULATORY ENGINE : SOURCES JURIDIQUES, VERSIONNAGE & RÈGLES
-- ============================================================================

-- Hiérarchie des sources juridiques (Section 20)
CREATE TABLE [reg].[LegalSources] (
    [Id]                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    -- Niveau 1=JORA, 2=DGD_ALCES, 3=MIN_FINANCES_DGI, 4=TEXTE_REGLEMENTAIRE, 5=AUTRE_INSTITUTION, 6=SOURCE_SECONDAIRE_AIDE
    [HierarchyLevel]     TINYINT          NOT NULL,
    [SourceType]         NVARCHAR(50)     NOT NULL,
    [OfficialTitle]      NVARCHAR(500)    NOT NULL,
    [JoraNumber]         NVARCHAR(50)     NULL, -- Ex: 'JORA N° 11 du 19/02/2017'
    [PublicationDate]    DATE             NOT NULL,
    [EffectiveDate]      DATE             NOT NULL,
    [ArticleReference]   NVARCHAR(150)    NULL, -- Ex: 'Art. 16 ter & 16 octies Code des Douanes'
    [OfficialUrl]        NVARCHAR(1000)   NULL,
    [DocumentHashSha256] CHAR(64)         NULL,
    [IsOfficialBinding]  BIT              NOT NULL DEFAULT 1, -- 0 pour niveau 6 (source secondaire)
    [CreatedAtUtc]       DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_LegalSources] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_LegalSources_Hierarchy] CHECK ([HierarchyLevel] BETWEEN 1 AND 6),
    CONSTRAINT [CK_LegalSources_SecondaryNotBinding] CHECK ([HierarchyLevel] < 6 OR [IsOfficialBinding] = 0)
);

-- Versions réglementaires globales (Section 19)
CREATE TABLE [reg].[RegulatoryVersions] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [VersionCode]         NVARCHAR(50)     NOT NULL, -- Ex: 'DZ-REG-2026.01'
    [Description]         NVARCHAR(500)    NOT NULL,
    [EffectiveFrom]       DATE             NOT NULL,
    [EffectiveTo]         DATE             NULL,
    -- Statut : 'DRAFT', 'PENDING_ADMIN_VALIDATION', 'PUBLISHED', 'SUPERSEDED'
    [Status]              NVARCHAR(30)     NOT NULL,
    [PrimaryLegalSourceId] UNIQUEIDENTIFIER NOT NULL,
    [ValidatedByUserId]   UNIQUEIDENTIFIER NULL,
    [ValidatedAtUtc]      DATETIME2(7)     NULL,
    [CreatedAtUtc]        DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_RegulatoryVersions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_RegulatoryVersions_Code] UNIQUE ([VersionCode]),
    CONSTRAINT [FK_RegulatoryVersions_LegalSource] FOREIGN KEY ([PrimaryLegalSourceId]) REFERENCES [reg].[LegalSources]([Id]),
    CONSTRAINT [CK_RegulatoryVersions_Status] CHECK ([Status] IN ('DRAFT', 'PENDING_ADMIN_VALIDATION', 'PUBLISHED', 'SUPERSEDED'))
);

-- Nomenclature Tarifaire Algérienne à 10 chiffres (Section 15)
CREATE TABLE [reg].[HsTariffCodes] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [HsCode10]            NVARCHAR(14)     NOT NULL, -- Ex: '8708999000' ou '8708.99.90.00'
    [HsCodeNormalized]    CHAR(10)         NOT NULL, -- 10 chiffres stricts sans points
    [SectionCode]         NVARCHAR(10)     NOT NULL,
    [ChapterCode]         CHAR(2)          NOT NULL,
    [HeadingCode]         CHAR(4)          NOT NULL,
    [SubHeading6]         CHAR(6)          NOT NULL, -- SH OMD 6 chiffres
    [NationalSubPosition] CHAR(4)          NOT NULL, -- 4 chiffres nationaux Algérie
    [DesignationFr]       NVARCHAR(1000)   NOT NULL,
    [DesignationAr]       NVARCHAR(1000)   NULL,
    [StatisticalUnitCode] NVARCHAR(20)     NOT NULL, -- UQN (kg, u, l...)
    [ValidFrom]           DATE             NOT NULL,
    [ValidTo]             DATE             NULL,
    [LegalSourceId]       UNIQUEIDENTIFIER NOT NULL,
    [RegulatoryVersionId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_HsTariffCodes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_HsTariffCodes_LegalSource] FOREIGN KEY ([LegalSourceId]) REFERENCES [reg].[LegalSources]([Id]),
    CONSTRAINT [FK_HsTariffCodes_Version] FOREIGN KEY ([RegulatoryVersionId]) REFERENCES [reg].[RegulatoryVersions]([Id])
);

CREATE INDEX [IX_HsTariffCodes_Normalized_Dates]
    ON [reg].[HsTariffCodes] ([HsCodeNormalized], [ValidFrom], [ValidTo]);

-- Régimes douaniers & accords préférentiels (Section 3 & 18)
CREATE TABLE [reg].[CustomsRegimes] (
    [Code]                NVARCHAR(30)     NOT NULL, -- Ex: 'DROIT_COMMUN_4000', 'PREF_UE', 'PREF_GZALE', 'PREF_ZLECAF', 'ANDI_AAPI'
    [NameFr]              NVARCHAR(250)    NOT NULL,
    [RequiresCertOrigin]  BIT              NOT NULL DEFAULT 0,
    [RequiresDirectTrans] BIT              NOT NULL DEFAULT 0,
    [LegalSourceId]       UNIQUEIDENTIFIER NOT NULL,
    [ValidFrom]           DATE             NOT NULL,
    [ValidTo]             DATE             NULL,
    CONSTRAINT [PK_CustomsRegimes] PRIMARY KEY CLUSTERED ([Code]),
    CONSTRAINT [FK_CustomsRegimes_LegalSource] FOREIGN KEY ([LegalSourceId]) REFERENCES [reg].[LegalSources]([Id])
);

-- Table centrale des Règles Réglementaires Versionnées (Sections 18, 19, 21, 23, 24)
-- AUCUN TAUX N'EST CODÉ EN DUR DANS C# : Tout passe par [reg].[RegulatoryRules]
CREATE TABLE [reg].[RegulatoryRules] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [RuleCode]            NVARCHAR(80)     NOT NULL,
    [RegulatoryVersionId] UNIQUEIDENTIFIER NOT NULL,
    [SupersedesRuleId]    UNIQUEIDENTIFIER NULL, -- Chaînage historique des versions (Section 19)
    -- Type de règle : 'CUSTOMS_DUTY' (DD), 'VAT' (TVA), 'DAPS', 'TIC', 'RDAE', 'TCS', 'SPECIFIC_TAX', 'EXEMPTION', 'VALUATION_RULE'
    [RuleType]            NVARCHAR(40)     NOT NULL,
    [TaxCode]             NVARCHAR(30)     NOT NULL, -- Ex: 'DD', 'TVA', 'DAPS', 'TIC', 'PRCT'
    [HsCodePattern]       NVARCHAR(14)     NOT NULL, -- Code 10 chiffres exact ou préfixe chapitre/position si applicable
    [OriginCountryIso2]   CHAR(2)          NULL,     -- NULL = Toutes origines (Droit commun)
    [ExportCountryIso2]   CHAR(2)          NULL,
    [CustomsRegimeCode]   NVARCHAR(30)     NOT NULL DEFAULT 'DROIT_COMMUN_4000',
    -- Mode de calcul : 'AD_VALOREM_PERCENT', 'SPECIFIC_PER_UNIT', 'EXEMPT', 'COMPOUND'
    [CalculationMode]     NVARCHAR(30)     NOT NULL DEFAULT 'AD_VALOREM_PERCENT',
    [RatePercent]         DECIMAL(9, 4)    NULL,     -- Ex: 15.0000 pour 15 %
    [SpecificAmountDzd]   DECIMAL(18, 4)   NULL,     -- Si taxe spécifique par unité/kg
    [SpecificUnit]        NVARCHAR(20)     NULL,
    -- Base de calcul codifiée :
    -- 'CUSTOMS_VALUE_DZD' (Valeur en douane — ex: DD, DAPS)
    -- 'CUSTOMS_VALUE_PLUS_DUTIES_AND_TAXES_EX_VAT' (Art. 19 CTCA : VD + DD + DAPS + TIC hors TVA)
    -- 'CIF_VALUE_DZD'
    [CalculationBaseCode] NVARCHAR(60)     NOT NULL,
    [ConditionExpression] NVARCHAR(1000)   NULL,     -- Expression JSON/DSL évaluable (ex: certificat EUR.1 requis)
    [ValidFrom]           DATE             NOT NULL,
    [ValidTo]             DATE             NULL,
    [Priority]            INT              NOT NULL DEFAULT 100,
    [LegalSourceId]       UNIQUEIDENTIFIER NOT NULL,
    [LegalArticleRef]     NVARCHAR(150)    NOT NULL, -- Ex: 'Art. 19 CTCA / Tarif Douanier'
    [JoraReference]       NVARCHAR(150)    NOT NULL,
    -- Workflow (Section 21) : 'DETECTED', 'AI_PROPOSED', 'PENDING_VALIDATION', 'PUBLISHED', 'ARCHIVED', 'REJECTED'
    [Status]              NVARCHAR(30)     NOT NULL DEFAULT 'PENDING_VALIDATION',
    [ProposedByAi]        BIT              NOT NULL DEFAULT 0,
    [ValidatedByAdminId]  UNIQUEIDENTIFIER NULL,
    [ValidatedAtUtc]      DATETIME2(7)     NULL,
    [CreatedAtUtc]        DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_RegulatoryRules] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_RegulatoryRules_Version] FOREIGN KEY ([RegulatoryVersionId]) REFERENCES [reg].[RegulatoryVersions]([Id]),
    CONSTRAINT [FK_RegulatoryRules_Supersedes] FOREIGN KEY ([SupersedesRuleId]) REFERENCES [reg].[RegulatoryRules]([Id]),
    CONSTRAINT [FK_RegulatoryRules_LegalSource] FOREIGN KEY ([LegalSourceId]) REFERENCES [reg].[LegalSources]([Id]),
    CONSTRAINT [FK_RegulatoryRules_Regime] FOREIGN KEY ([CustomsRegimeCode]) REFERENCES [reg].[CustomsRegimes]([Code]),
    CONSTRAINT [FK_RegulatoryRules_Admin] FOREIGN KEY ([ValidatedByAdminId]) REFERENCES [core].[Users]([Id]),
    CONSTRAINT [CK_RegulatoryRules_Status] CHECK ([Status] IN ('DETECTED', 'AI_PROPOSED', 'PENDING_VALIDATION', 'PUBLISHED', 'ARCHIVED', 'REJECTED')),
    -- Règle de sécurité (Section 21 & 39) : Une règle PUBLISHED doit obligatoirement avoir un ValidatedByAdminId
    CONSTRAINT [CK_RegulatoryRules_PublishRequiresAdmin] CHECK ([Status] <> 'PUBLISHED' OR [ValidatedByAdminId] IS NOT NULL),
    CONSTRAINT [CK_RegulatoryRules_Dates] CHECK ([ValidTo] IS NULL OR [ValidTo] >= [ValidFrom])
);

CREATE INDEX [IX_RegulatoryRules_Resolution]
    ON [reg].[RegulatoryRules] ([Status], [HsCodePattern], [RuleType], [CustomsRegimeCode], [ValidFrom], [ValidTo])
    INCLUDE ([RatePercent], [CalculationBaseCode], [OriginCountryIso2], [Priority], [LegalSourceId]);

-- ============================================================================
-- 4. MODULE EXCEL ENGINE : MODÈLES DE MAPPING & SYNONYMES D'EN-TÊTES
-- ============================================================================

-- Dictionnaire de synonymes pour la détection automatique d'en-têtes (Section 4)
CREATE TABLE [excel].[ColumnHeaderSynonyms] (
    [Id]                  INT IDENTITY(1,1) NOT NULL,
    [CompanyId]           UNIQUEIDENTIFIER  NULL,     -- NULL = Dictionnaire système global, sinon spécifique entreprise
    [RawHeaderNormalized] NVARCHAR(150)     NOT NULL, -- Ex: 'REF', 'PRIX ACHAT', 'CODE DOUANE', 'SH'
    -- Champ cible canonique : 'ProductReference', 'Designation', 'Quantity', 'UnitPrice', 'Currency',
    -- 'HsCode', 'OriginCountry', 'ExcelDutyRate', 'Incoterm', 'GrossWeight', 'NetWeight', 'Volume', 'Freight', 'Insurance'
    [TargetCanonicalField] NVARCHAR(80)     NOT NULL,
    [ConfidenceScore]     DECIMAL(5, 2)     NOT NULL DEFAULT 100.00,
    CONSTRAINT [PK_ColumnHeaderSynonyms] PRIMARY KEY CLUSTERED ([Id])
);

-- Modèles de mapping enregistrés par fournisseur/entreprise (Section 5)
CREATE TABLE [excel].[ExcelMappingTemplates] (
    [Id]                    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CompanyId]             UNIQUEIDENTIFIER NOT NULL,
    [SupplierId]            UNIQUEIDENTIFIER NULL,
    [TemplateName]          NVARCHAR(150)    NOT NULL, -- Ex: 'FOURNISSEUR_X'
    [HeaderSignatureHash]   CHAR(64)         NOT NULL, -- Hash SHA-256 des colonnes pour reconnaissance auto
    [HeaderRowIndex]        INT              NOT NULL DEFAULT 1,
    [FirstDataRowIndex]     INT              NOT NULL DEFAULT 2,
    [WorksheetName]         NVARCHAR(128)    NULL,
    [ColumnMappingsJson]    NVARCHAR(MAX)    NOT NULL, -- Ex: [{"ColumnLetter":"B","RawHeader":"REF","TargetField":"ProductReference"}, ...]
    [CreatedByUserId]       UNIQUEIDENTIFIER NOT NULL,
    [CreatedAtUtc]          DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ExcelMappingTemplates] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ExcelMappingTemplates_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies]([Id]),
    CONSTRAINT [FK_ExcelMappingTemplates_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [core].[Suppliers]([Id]),
    CONSTRAINT [UQ_ExcelMappingTemplates_Company_Name] UNIQUE ([CompanyId], [TemplateName])
);

-- ============================================================================
-- 5. MODULE CORE IMPORTS : DOSSIERS D'IMPORTATION, LIGNES, FRAIS & CONTENEURS
-- ============================================================================

CREATE TABLE [core].[ImportOperations] (
    [Id]                          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CompanyId]                   UNIQUEIDENTIFIER NOT NULL,
    [ImportNumber]                NVARCHAR(50)     NOT NULL,
    -- Date de référence réglementaire (ex: Date d'enregistrement de la déclaration en détail - Art. 16 decies & Art. 103 CDA)
    [ReferenceDate]               DATE             NOT NULL,
    [SupplierId]                  UNIQUEIDENTIFIER NULL,
    [SupplierNameSnapshot]        NVARCHAR(250)    NOT NULL,
    [DefaultOriginCountryIso2]    CHAR(2)          NULL,     -- Origine par défaut (surchargée par ligne si multi-origine)
    [ExportShippingCountryIso2]   CHAR(2)          NOT NULL, -- Pays d'expédition saisi UNE SEULE FOIS (Section 6)
    [MainCurrencyCode]            CHAR(3)          NOT NULL,
    [AppliedExchangeRateToDzd]    DECIMAL(18, 6)   NOT NULL,
    [ExchangeRateId]              UNIQUEIDENTIFIER NULL,     -- Référence au taux réglementaire ALCES
    [IsManualExchangeRate]        BIT              NOT NULL DEFAULT 0, -- Section 14 : Déclenche ⚠️ TAUX MANUEL
    [RegulatoryExchangeRateSnapshot] DECIMAL(18, 6) NULL,    -- Conserve le taux officiel de comparaison si manuel
    [IncotermCode]                NVARCHAR(10)     NOT NULL, -- V1 : EXW, FOB, CFR
    [CustomsRegimeCode]           NVARCHAR(30)     NOT NULL DEFAULT 'DROIT_COMMUN_4000',
    -- Méthode d'évaluation douanière (Section 22) : 'TRANSACTION_VALUE_ART_16_TER' par défaut
    [CustomsValuationMethod]      NVARCHAR(50)     NOT NULL DEFAULT 'TRANSACTION_VALUE_ART_16_TER',
    [ArrivalPortOrBorder]         NVARCHAR(150)    NOT NULL, -- Lieu d'introduction dans le territoire douanier algérien
    [TransportMode]               NVARCHAR(40)     NOT NULL, -- 'MARITIME', 'AERIEN', 'ROUTIER', 'MULTIMODAL'
    [SingleContainerOrTransportCost] DECIMAL(18, 4) NULL,    -- Section 10 : Saisi globalement en V1
    [TransportCostCurrency]       CHAR(3)          NULL,
    [ExcelMappingTemplateId]      UNIQUEIDENTIFIER NULL,
    [SourceExcelFileName]         NVARCHAR(260)    NULL,
    [IsSimulation]                BIT              NOT NULL DEFAULT 0, -- Section 30 : 1 tant que non enregistré comme importation réelle
    [ParentImportIdForSimulation] UNIQUEIDENTIFIER NULL,
    -- Statut : 'DRAFT', 'MAPPING_REQUIRED', 'READY_TO_CALCULATE', 'CALCULATED_WITH_WARNINGS', 'BLOCKED_BY_ANOMALIES', 'VALIDATED'
    [Status]                      NVARCHAR(40)     NOT NULL DEFAULT 'DRAFT',
    [CreatedByUserId]             UNIQUEIDENTIFIER NOT NULL,
    [CreatedAtUtc]                DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc]                DATETIME2(7)     NULL,
    CONSTRAINT [PK_ImportOperations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ImportOperations_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies]([Id]),
    CONSTRAINT [FK_ImportOperations_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [core].[Suppliers]([Id]),
    CONSTRAINT [FK_ImportOperations_ShippingCountry] FOREIGN KEY ([ExportShippingCountryIso2]) REFERENCES [core].[Countries]([IsoAlpha2]),
    CONSTRAINT [FK_ImportOperations_Currency] FOREIGN KEY ([MainCurrencyCode]) REFERENCES [core].[Currencies]([Code]),
    CONSTRAINT [FK_ImportOperations_Incoterm] FOREIGN KEY ([IncotermCode]) REFERENCES [core].[Incoterms]([Code]),
    CONSTRAINT [FK_ImportOperations_Regime] FOREIGN KEY ([CustomsRegimeCode]) REFERENCES [reg].[CustomsRegimes]([Code]),
    CONSTRAINT [UQ_ImportOperations_Company_Number] UNIQUE ([CompanyId], [ImportNumber])
);

-- Architecture extensible pour plusieurs conteneurs en V2 (Section 10)
CREATE TABLE [core].[ImportContainers] (
    [Id]                UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [ImportOperationId] UNIQUEIDENTIFIER NOT NULL,
    [ContainerNumber]   NVARCHAR(30)     NULL,
    [ContainerType]     NVARCHAR(20)     NOT NULL DEFAULT '20DC', -- 20DC, 40DC, 40HC, LCL
    [TransportCost]     DECIMAL(18, 4)   NOT NULL,
    [CurrencyCode]      CHAR(3)          NOT NULL,
    [GrossWeightKg]     DECIMAL(18, 4)   NULL,
    [VolumeM3]          DECIMAL(18, 6)   NULL,
    CONSTRAINT [PK_ImportContainers] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ImportContainers_Operation] FOREIGN KEY ([ImportOperationId]) REFERENCES [core].[ImportOperations]([Id]) ON DELETE CASCADE
);

-- Lignes d'importation (Articles / Marchandises — Sections 6, 15, 17, 27)
CREATE TABLE [core].[ImportLines] (
    [Id]                          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [ImportOperationId]           UNIQUEIDENTIFIER NOT NULL,
    [LineNumber]                  INT              NOT NULL,
    [ProductId]                   UNIQUEIDENTIFIER NULL,
    [ProductReference]            NVARCHAR(100)    NOT NULL,
    [Designation]                 NVARCHAR(500)    NOT NULL,
    [Quantity]                    DECIMAL(18, 4)   NOT NULL,
    [MeasurementUnit]             NVARCHAR(20)     NOT NULL DEFAULT 'U',
    [UnitPurchasePriceCurrency]   DECIMAL(18, 6)   NOT NULL,
    [TotalPurchasePriceCurrency]  DECIMAL(18, 4)   NOT NULL,
    [CurrencyCode]                CHAR(3)          NOT NULL,
    [AppliedExchangeRateToDzd]    DECIMAL(18, 6)   NOT NULL,
    [HsCodeExcel]                 NVARCHAR(14)     NULL,     -- Code SH issu du fichier Excel
    [HsCodeConfirmed10]           NVARCHAR(14)     NULL,     -- Code SH confirmé pour le calcul réglementaire
    [AiProposedHsCode10]          NVARCHAR(14)     NULL,     -- Proposition IA (Section 16)
    -- Statut validation SH IA : 'NOT_APPLICABLE', 'PROPOSED_PENDING_USER', 'CONFIRMED_BY_USER', 'MODIFIED_BY_USER', 'REJECTED_BY_USER'
    [AiHsValidationStatus]        NVARCHAR(30)     NOT NULL DEFAULT 'NOT_APPLICABLE',
    [OriginCountryIso2]           CHAR(2)          NULL,     -- Pays d'origine par ligne (Section 6)
    [ExcelDutyRatePercent]        DECIMAL(9, 4)    NULL,     -- Source 1 : Droit de douane Excel (Section 17)
    [RegulatoryDutyRatePercent]   DECIMAL(9, 4)    NULL,     -- Source 2 : Droit réglementaire trouvé
    -- Statut comparaison DD : 'MATCH', 'DIFFERENCE', 'REGULATORY_NOT_FOUND_PENDING_CONFIRMATION', 'EXCEL_ONLY_CONFIRMED'
    [DutyComparisonStatus]        NVARCHAR(50)     NOT NULL DEFAULT 'MATCH',
    [UserConfirmedExcelDutyFallback] BIT           NOT NULL DEFAULT 0,
    [LineGrossWeightKg]           DECIMAL(18, 4)   NULL,     -- Facultatif en V1 (Section 12)
    [LineNetWeightKg]             DECIMAL(18, 4)   NULL,
    [LineVolumeM3]                DECIMAL(18, 6)   NULL,     -- Facultatif en V1
    CONSTRAINT [PK_ImportLines] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ImportLines_Operation] FOREIGN KEY ([ImportOperationId]) REFERENCES [core].[ImportOperations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ImportLines_Product] FOREIGN KEY ([ProductId]) REFERENCES [core].[Products]([Id]),
    CONSTRAINT [FK_ImportLines_Currency] FOREIGN KEY ([CurrencyCode]) REFERENCES [core].[Currencies]([Code]),
    CONSTRAINT [FK_ImportLines_Origin] FOREIGN KEY ([OriginCountryIso2]) REFERENCES [core].[Countries]([IsoAlpha2]),
    CONSTRAINT [CK_ImportLines_QuantityPositive] CHECK ([Quantity] > 0),
    CONSTRAINT [UQ_ImportLines_Operation_LineNo] UNIQUE ([ImportOperationId], [LineNumber])
);

-- Frais liés à l'importation et règles de répartition par frais (Sections 9 & 11)
CREATE TABLE [core].[ImportFees] (
    [Id]                          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [ImportOperationId]           UNIQUEIDENTIFIER NOT NULL,
    -- Catégorie : 'FRET_INTERNATIONAL', 'ASSURANCE', 'TRANSPORT_INTERIEUR_EXPORT', 'FRAIS_EXPORT',
    -- 'MANUTENTION_EXPORT', 'MANUTENTION_LOCALE', 'FRAIS_PORTUAIRES', 'THC', 'MAGASINAGE', 'DEPOTAGE',
    -- 'TRANSIT', 'COMMISSIONNAIRE_DOUANE', 'TRANSPORT_PORT_ENTREPOT', 'FRAIS_BANCAIRES', 'DOMICILIATION',
    -- 'CONTROLE', 'INSPECTION', 'CERTIFICATION', 'AUTRES_FRAIS', 'PERSONNALISE'
    [FeeCategoryCode]             NVARCHAR(50)     NOT NULL,
    [CustomFeeName]               NVARCHAR(200)    NOT NULL,
    [AmountInCurrency]            DECIMAL(18, 4)   NOT NULL,
    [CurrencyCode]                CHAR(3)          NOT NULL,
    [AppliedExchangeRateToDzd]    DECIMAL(18, 6)   NOT NULL,
    [AmountDzd]                   DECIMAL(18, 4)   NOT NULL,
    [CalculationBaseDescription]  NVARCHAR(150)    NULL,
    -- Méthode de répartition (Section 11) :
    -- 'BY_VALUE', 'BY_QUANTITY', 'BY_WEIGHT', 'BY_VOLUME', 'FIXED_AMOUNT', 'PERCENTAGE', 'MANUAL'
    [AllocationMethod]            NVARCHAR(30)     NOT NULL,
    -- Inclusion réglementaire dans la Valeur en Douane (Art. 16 octies CDA)
    [IncludeInCustomsValue]       BIT              NOT NULL,
    -- Type d'ajustement douanier : 'ADDITION_ART_16_OCTIES', 'DEDUCTION_ART_16_TER', 'POST_INTRODUCTION_EXCLUDED'
    [CustomsValuationTreatment]   NVARCHAR(40)     NOT NULL DEFAULT 'POST_INTRODUCTION_EXCLUDED',
    -- Inclusion dans le Coût de Revient économique (Section 9 & 26)
    [IncludeInCostOfGoods]        BIT              NOT NULL DEFAULT 1,
    [LegalBasisReference]         NVARCHAR(250)    NULL,
    CONSTRAINT [PK_ImportFees] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ImportFees_Operation] FOREIGN KEY ([ImportOperationId]) REFERENCES [core].[ImportOperations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ImportFees_Currency] FOREIGN KEY ([CurrencyCode]) REFERENCES [core].[Currencies]([Code]),
    CONSTRAINT [CK_ImportFees_AllocationMethod] CHECK ([AllocationMethod] IN (
        'BY_VALUE', 'BY_QUANTITY', 'BY_WEIGHT', 'BY_VOLUME', 'FIXED_AMOUNT', 'PERCENTAGE', 'MANUAL'
    ))
);

-- ============================================================================
-- 6. MODULE CALCULATION ENGINE : RÉSULTATS DÉTAILLÉS, TAXES & ANOMALIES
-- ============================================================================

-- Exécution d'un calcul consolidé (Sections 25, 26, 27)
CREATE TABLE [calc].[CalculationRuns] (
    [Id]                              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [ImportOperationId]               UNIQUEIDENTIFIER NOT NULL,
    [RegulatoryVersionId]             UNIQUEIDENTIFIER NOT NULL,
    [RunNumber]                       INT              NOT NULL,
    [CalculationTimestampUtc]         DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    [ExecutedByUserId]                UNIQUEIDENTIFIER NOT NULL,
    -- Totaux consolidés de l'importation (11 indicateurs clés - Section 1)
    [TotalPurchaseValueCurrency]      DECIMAL(18, 4)   NOT NULL,
    [TotalPurchaseValueDzd]           DECIMAL(18, 4)   NOT NULL,
    [TotalCustomsAdditionsDzd]        DECIMAL(18, 4)   NOT NULL,
    [TotalCustomsDeductionsDzd]       DECIMAL(18, 4)   NOT NULL,
    [TotalCustomsValueDzd]            DECIMAL(18, 4)   NOT NULL, -- Valeur en douane totale
    [TotalCustomsDutyDzd]             DECIMAL(18, 4)   NOT NULL, -- Total Droits de Douane (DD)
    [TotalOtherTaxesAndLeviesDzd]     DECIMAL(18, 4)   NOT NULL, -- Total DAPS, TIC, RDAE, TCS...
    [TotalImportVatDzd]               DECIMAL(18, 4)   NOT NULL, -- Total TVA à l'importation
    -- RÉSULTAT 1 (Section 25) : Coût / Total Droits et Taxes douaniers
    [TotalCustomsDutiesAndTaxesDzd]   DECIMAL(18, 4)   NOT NULL,
    [TotalCustomsClearedValueDzd]     DECIMAL(18, 4)   NOT NULL, -- Valeur en douane + Total droits et taxes
    -- Frais locaux / hors valeur en douane mais inclus dans le coût de revient
    [TotalLocalImportFeesDzd]         DECIMAL(18, 4)   NOT NULL,
    [TotalAllImportFeesDzd]           DECIMAL(18, 4)   NOT NULL,
    -- RÉSULTAT 2 (Section 26) : Coût d'acquisition & Coût de revient économique réel
    [TotalAcquisitionCostExVatDzd]    DECIMAL(18, 4)   NOT NULL,
    [TotalRealCostOfGoodsDzd]         DECIMAL(18, 4)   NOT NULL, -- Coût total de l'importation (TVA non récupérable incluse si paramètre actif)
    [HasBlockingAnomalies]            BIT              NOT NULL DEFAULT 0,
    [LegalDisclaimerText]             NVARCHAR(1000)   NOT NULL, -- Mention légale obligatoire (Section 40)
    CONSTRAINT [PK_CalculationRuns] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_CalculationRuns_Operation] FOREIGN KEY ([ImportOperationId]) REFERENCES [core].[ImportOperations]([Id]),
    CONSTRAINT [FK_CalculationRuns_RegVersion] FOREIGN KEY ([RegulatoryVersionId]) REFERENCES [reg].[RegulatoryVersions]([Id]),
    CONSTRAINT [FK_CalculationRuns_User] FOREIGN KEY ([ExecutedByUserId]) REFERENCES [core].[Users]([Id])
);

-- Résultat de calcul ligne par ligne (Section 27)
CREATE TABLE [calc].[CalculationLineResults] (
    [Id]                              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CalculationRunId]                UNIQUEIDENTIFIER NOT NULL,
    [ImportLineId]                    UNIQUEIDENTIFIER NOT NULL,
    [LineNumber]                      INT              NOT NULL,
    [ProductReference]                NVARCHAR(100)    NOT NULL,
    [Quantity]                        DECIMAL(18, 4)   NOT NULL,
    -- 1. Valeur d'achat en devise
    [PurchaseValueCurrency]           DECIMAL(18, 4)   NOT NULL,
    -- 2. Valeur d'achat convertie en DZD
    [PurchaseValueDzd]                DECIMAL(18, 4)   NOT NULL,
    -- Ajustements douaniers répartis sur la ligne (Fret, Assurance, pré-acheminement EXW...)
    [AllocatedCustomsAdditionsDzd]    DECIMAL(18, 4)   NOT NULL,
    [AllocatedCustomsDeductionsDzd]   DECIMAL(18, 4)   NOT NULL,
    -- 3. Valeur en douane de la ligne (DZD)
    [CustomsValueDzd]                 DECIMAL(18, 4)   NOT NULL,
    -- 4. Droits de douane (DD)
    [AppliedCustomsDutyRatePercent]   DECIMAL(9, 4)    NOT NULL,
    [CustomsDutyRuleId]               UNIQUEIDENTIFIER NULL,
    [CustomsDutyDzd]                  DECIMAL(18, 4)   NOT NULL,
    -- 5. Autres taxes et prélèvements applicables (DAPS, TIC, TCS...)
    [OtherTaxesAndLeviesDzd]          DECIMAL(18, 4)   NOT NULL,
    -- 6. TVA à l'importation (Assiette Art. 19 CTCA = VD + DD + Autres taxes hors TVA)
    [VatTaxableBaseDzd]               DECIMAL(18, 4)   NOT NULL,
    [AppliedVatRatePercent]           DECIMAL(9, 4)    NOT NULL,
    [VatRuleId]                       UNIQUEIDENTIFIER NULL,
    [ImportVatDzd]                    DECIMAL(18, 4)   NOT NULL,
    -- Résultat Douanier de la ligne (Section 25)
    [TotalLineDutiesAndTaxesDzd]      DECIMAL(18, 4)   NOT NULL,
    [LineCustomsTotalDzd]             DECIMAL(18, 4)   NOT NULL, -- VD + DD + Taxes + TVA
    -- 7. Frais liés à l'importation alloués à la ligne (frais locaux hors VD + frais dans VD)
    [AllocatedLocalFeesDzd]           DECIMAL(18, 4)   NOT NULL,
    [TotalAllocatedFeesDzd]           DECIMAL(18, 4)   NOT NULL,
    -- 8. Coût d'acquisition HT (Hors TVA)
    [AcquisitionCostExVatDzd]         DECIMAL(18, 4)   NOT NULL,
    -- 9. Coût de revient réel total de la ligne (Section 26 : intègre la TVA non récupérable)
    [RealCostOfGoodsTotalDzd]         DECIMAL(18, 4)   NOT NULL,
    -- 10. Coût de revient unitaire de l'article
    [UnitCostOfGoodsDzd]              DECIMAL(18, 4)   NOT NULL,
    -- Coefficient d'approche (Coût de revient DZD / Valeur d'achat DZD)
    [LandedCostMultiplier]            DECIMAL(12, 6)   NOT NULL,
    CONSTRAINT [PK_CalculationLineResults] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_CalcLineResults_Run] FOREIGN KEY ([CalculationRunId]) REFERENCES [calc].[CalculationRuns]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CalcLineResults_Line] FOREIGN KEY ([ImportLineId]) REFERENCES [core].[ImportLines]([Id]),
    CONSTRAINT [FK_CalcLineResults_DutyRule] FOREIGN KEY ([CustomsDutyRuleId]) REFERENCES [reg].[RegulatoryRules]([Id]),
    CONSTRAINT [FK_CalcLineResults_VatRule] FOREIGN KEY ([VatRuleId]) REFERENCES [reg].[RegulatoryRules]([Id])
);

-- Détail traçable de chaque taxe appliquée par ligne (Section 23 & 45)
CREATE TABLE [calc].[CalculationLineTaxDetails] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CalculationLineId]   UNIQUEIDENTIFIER NOT NULL,
    [RegulatoryRuleId]    UNIQUEIDENTIFIER NOT NULL,
    [TaxCode]             NVARCHAR(30)     NOT NULL, -- 'DD', 'DAPS', 'TIC', 'TVA'...
    [TaxNameFr]           NVARCHAR(150)    NOT NULL,
    [TaxableBaseDzd]      DECIMAL(18, 4)   NOT NULL,
    [AppliedRatePercent]  DECIMAL(9, 4)    NULL,
    [TaxAmountDzd]        DECIMAL(18, 4)   NOT NULL,
    [IsRecoverable]       BIT              NOT NULL DEFAULT 0,
    [LegalArticleRef]     NVARCHAR(150)    NOT NULL,
    [JoraReference]       NVARCHAR(150)    NOT NULL,
    CONSTRAINT [PK_CalculationLineTaxDetails] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_CalcLineTax_CalcLine] FOREIGN KEY ([CalculationLineId]) REFERENCES [calc].[CalculationLineResults]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CalcLineTax_Rule] FOREIGN KEY ([RegulatoryRuleId]) REFERENCES [reg].[RegulatoryRules]([Id])
);

-- Traçabilité de la répartition des frais par ligne (Section 11)
CREATE TABLE [calc].[CalculationFeeAllocations] (
    [Id]                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CalculationLineId]       UNIQUEIDENTIFIER NOT NULL,
    [ImportFeeId]             UNIQUEIDENTIFIER NOT NULL,
    [FeeName]                 NVARCHAR(200)    NOT NULL,
    [AllocationMethodUsed]    NVARCHAR(30)     NOT NULL,
    [AllocationWeightOrRatio] DECIMAL(18, 8)   NOT NULL, -- Part de la ligne (ex: 0.62500000)
    [AllocatedAmountDzd]      DECIMAL(18, 4)   NOT NULL,
    [IncludedInCustomsValue]  BIT              NOT NULL,
    [IncludedInCostOfGoods]   BIT              NOT NULL,
    CONSTRAINT [PK_CalculationFeeAllocations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_CalcFeeAlloc_CalcLine] FOREIGN KEY ([CalculationLineId]) REFERENCES [calc].[CalculationLineResults]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CalcFeeAlloc_ImportFee] FOREIGN KEY ([ImportFeeId]) REFERENCES [core].[ImportFees]([Id])
);

-- Moteur de contrôle et d'anomalies (Section 28)
CREATE TABLE [calc].[CalculationAnomalies] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CalculationRunId]    UNIQUEIDENTIFIER NOT NULL,
    [ImportOperationId]   UNIQUEIDENTIFIER NOT NULL,
    [ImportLineId]        UNIQUEIDENTIFIER NULL,     -- NULL si anomalie globale au dossier
    -- Sévérité : 'INFO', 'AVERTISSEMENT', 'ERREUR', 'BLOCAGE'
    [Severity]            NVARCHAR(20)     NOT NULL,
    -- Code anomalie : 'MISSING_HS_CODE', 'MISSING_ORIGIN', 'UNKNOWN_CURRENCY', 'MISSING_EXCHANGE_RATE',
    -- 'MANUAL_EXCHANGE_RATE_DIFF', 'EXCEL_VS_REGULATORY_DUTY_DIFF', 'UNCONFIRMED_AI_HS_CODE',
    -- 'EXW_MISSING_REQUIRED_FEES', 'CFR_MISSING_INSURANCE', 'EXPIRED_REGULATORY_RULE',
    -- 'REGULATORY_RULE_NOT_FOUND', 'UNDETERMINED_APPLICABLE_TAX', 'INCONSISTENT_DATA'
    [AnomalyCode]         NVARCHAR(60)     NOT NULL,
    [MessageFr]           NVARCHAR(1000)   NOT NULL,
    [ExpectedValue]       NVARCHAR(200)    NULL,
    [ActualValue]         NVARCHAR(200)    NULL,
    [IsResolved]          BIT              NOT NULL DEFAULT 0,
    [ResolvedByUserId]    UNIQUEIDENTIFIER NULL,
    [ResolutionNote]      NVARCHAR(500)    NULL,
    [CreatedAtUtc]        DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_CalculationAnomalies] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_CalcAnomalies_Run] FOREIGN KEY ([CalculationRunId]) REFERENCES [calc].[CalculationRuns]([Id]) ON DELETE CASCADE,
    CONSTRAINT [CK_CalcAnomalies_Severity] CHECK ([Severity] IN ('INFO', 'AVERTISSEMENT', 'ERREUR', 'BLOCAGE'))
);

-- ============================================================================
-- 7. MODULE AI : PROPOSITIONS DE CLASSIFICATION SH & VEILLE RÉGLEMENTAIRE
-- ============================================================================

CREATE TABLE [ai].[HsClassificationProposals] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    [CompanyId]           UNIQUEIDENTIFIER NOT NULL,
    [ImportLineId]        UNIQUEIDENTIFIER NULL,
    [ProductId]           UNIQUEIDENTIFIER NULL,
    [InputReference]      NVARCHAR(100)    NOT NULL,
    [InputDesignation]    NVARCHAR(500)    NOT NULL,
    [InputOriginCountry]  CHAR(2)          NULL,
    [ProposedHsCode10]    NVARCHAR(14)     NOT NULL,
    [ProposedDescription] NVARCHAR(500)    NOT NULL,
    [ConfidencePercent]   DECIMAL(5, 2)    NOT NULL, -- Ex: 87.00 %
    [Justification]       NVARCHAR(MAX)    NOT NULL,
    [GeneralInterpretRule] NVARCHAR(150)   NOT NULL, -- Ex: 'RGI 1 et RGI 6 (Parties et accessoires)'
    -- Décision utilisateur obligatoire (Section 16) : 'PENDING', 'CONFIRMED', 'MODIFIED', 'REJECTED'
    [UserDecision]        NVARCHAR(20)     NOT NULL DEFAULT 'PENDING',
    [FinalUserHsCode10]   NVARCHAR(14)     NULL,
    [DecidedByUserId]     UNIQUEIDENTIFIER NULL,
    [DecidedAtUtc]        DATETIME2(7)     NULL,
    [CreatedAtUtc]        DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_HsClassificationProposals] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_HsProposals_Decision] CHECK ([UserDecision] IN ('PENDING', 'CONFIRMED', 'MODIFIED', 'REJECTED'))
);

-- ============================================================================
-- 8. MODULE AUDIT : JOURNAL D'AUDIT IMMUABLE (SECTION 34 & 39)
-- ============================================================================

CREATE TABLE [audit].[AuditLogs] (
    [Id]                   BIGINT IDENTITY(1,1) NOT NULL,
    [CompanyId]            UNIQUEIDENTIFIER     NULL,
    [UserId]               UNIQUEIDENTIFIER     NOT NULL,
    [ActionTimestampUtc]   DATETIME2(7)         NOT NULL DEFAULT SYSUTCDATETIME(),
    [ActionCategory]       NVARCHAR(50)         NOT NULL, -- 'REGULATORY_RULE', 'EXCHANGE_RATE', 'CALCULATION', 'HS_VALIDATION', 'IMPORT_OPERATION'
    [ActionName]           NVARCHAR(100)        NOT NULL,
    [ImportOperationId]    UNIQUEIDENTIFIER     NULL,
    [ProductId]            UNIQUEIDENTIFIER     NULL,
    [RegulatoryRuleId]     UNIQUEIDENTIFIER     NULL,
    [CalculationRunId]     UNIQUEIDENTIFIER     NULL,
    [OldValueJson]         NVARCHAR(MAX)        NULL,
    [NewValueJson]         NVARCHAR(MAX)        NULL,
    [LegalSourceReference] NVARCHAR(300)        NULL,
    [RegulatoryVersion]    NVARCHAR(50)         NULL,
    [ReasonOrComment]      NVARCHAR(500)        NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id])
);

CREATE INDEX [IX_AuditLogs_Company_Timestamp]
    ON [audit].[AuditLogs] ([CompanyId], [ActionTimestampUtc] DESC);
GO
