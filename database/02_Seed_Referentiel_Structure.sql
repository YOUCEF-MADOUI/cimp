/* ============================================================================
 * PROJET : ImportCost Algeria
 * FICHIER : database/02_Seed_Referentiel_Structure.sql
 * OBJET   : Données de paramétrage structurel (Devises, Incoterms V1/V2,
 *           Champs dynamiques par Incoterm, Synonymes d'en-têtes Excel,
 *           Sources juridiques officielles algériennes de référence)
 * ============================================================================ */

-- 1. DEVISES (Section 13)
INSERT INTO [core].[Currencies] ([Code], [NumericCode], [NameFr], [Symbol], [QuotityUnit], [DecimalPlaces], [IsActive])
VALUES
    ('DZD', '012', N'Dinar algérien',            N'DA',  1, 2, 1),
    ('EUR', '978', N'Euro',                      N'€',   1, 2, 1),
    ('USD', '840', N'Dollar américain',          N'$',   1, 2, 1),
    ('GBP', '826', N'Livre sterling',            N'£',   1, 2, 1),
    ('CNY', '156', N'Yuan renminbi chinois',     N'¥',   1, 2, 1),
    ('AED', '784', N'Dirham des Émirats arabes', N'AED', 1, 2, 1),
    ('TRY', '949', N'Livre turque',              N'₺',   1, 2, 1),
    ('CHF', '756', N'Franc suisse',              N'CHF', 1, 2, 1),
    ('CAD', '124', N'Dollar canadien',           N'CA$', 1, 2, 1),
    ('JPY', '392', N'Yen japonais',              N'¥', 100, 0, 1);

-- 2. INCOTERMS V1 (EXW, FOB, CFR) ET PRÉPARATION ARCHITECTURE V2 (Sections 7 & 8)
INSERT INTO [core].[Incoterms]
([Code], [IncotermVersion], [NameFr], [IsSupportedInV1], [RequiresPreCarriage], [RequiresExportClearance], [RequiresMainFreight], [RequiresInsurance], [FreightAlreadyInPrice], [InsuranceAlreadyInPrice], [LegalNote])
VALUES
    ('EXW', 'ICC_2020', N'Ex Works (À l''usine)',                 1, 1, 1, 1, 1, 0, 0, N'Art. 16 octies §1 e) CDA : Ajouter pré-acheminement, frais export, manutention chargement, fret international et assurance jusqu''au lieu d''introduction en Algérie.'),
    ('FOB', 'ICC_2020', N'Free On Board (Franco à bord)',         1, 0, 0, 1, 1, 0, 0, N'Art. 16 octies §1 e) CDA : Ajouter fret international et assurance jusqu''au lieu d''introduction en Algérie.'),
    ('CFR', 'ICC_2020', N'Cost and Freight (Coût et fret)',       1, 0, 0, 0, 1, 1, 0, N'Art. 16 octies §1 e) CDA : Fret inclus dans le prix CFR ; ajouter l''assurance transport jusqu''au lieu d''introduction en Algérie.'),
    ('FCA', 'ICC_2020', N'Free Carrier (Franco transporteur)',    0, 0, 0, 1, 1, 0, 0, N'Prévu pour extension post-V1.'),
    ('FAS', 'ICC_2020', N'Free Alongside Ship',                   0, 0, 0, 1, 1, 0, 0, N'Prévu pour extension post-V1.'),
    ('CIF', 'ICC_2020', N'Cost, Insurance and Freight',           0, 0, 0, 0, 0, 1, 1, N'Prévu pour extension post-V1.'),
    ('CPT', 'ICC_2020', N'Carriage Paid To',                      0, 0, 0, 0, 1, 1, 0, N'Prévu pour extension post-V1.'),
    ('CIP', 'ICC_2020', N'Carriage and Insurance Paid To',        0, 0, 0, 0, 0, 1, 1, N'Prévu pour extension post-V1.'),
    ('DAP', 'ICC_2020', N'Delivered At Place',                    0, 0, 0, 0, 0, 1, 1, N'Prévu pour extension post-V1 (déduction éventuelle post-introduction selon Art. 16 octies §3 CDA).'),
    ('DPU', 'ICC_2020', N'Delivered at Place Unloaded',           0, 0, 0, 0, 0, 1, 1, N'Prévu pour extension post-V1.'),
    ('DDP', 'ICC_2020', N'Delivered Duty Paid',                   0, 0, 0, 0, 0, 1, 1, N'Prévu pour extension post-V1.');

-- 3. RÈGLES DE CHAMPS DYNAMIQUES PAR INCOTERM V1 (Section 8 & 28)
INSERT INTO [core].[IncotermFieldRules]
([IncotermCode], [FeeCategoryCode], [FieldLabelFr], [IsRequiredForCustomsValue], [IsDisplayedByDefault], [CustomsAdjustmentType], [AnomalySeverityIfMissing], [RegulatoryJustificationRef], [DisplayOrder])
VALUES
    -- EXW
    ('EXW', 'TRANSPORT_INTERIEUR_EXPORT', N'Transport intérieur pays exportateur', 1, 1, 'ADD_TO_CUSTOMS_VALUE', 'AVERTISSEMENT', N'Code des Douanes Algérien Art. 16 octies §1 e) i)',  1),
    ('EXW', 'FRAIS_EXPORT',               N'Frais de dédouanement export',         1, 1, 'ADD_TO_CUSTOMS_VALUE', 'AVERTISSEMENT', N'Code des Douanes Algérien Art. 16 octies §1 e) ii)', 2),
    ('EXW', 'MANUTENTION_EXPORT',         N'Manutention / chargement export',      1, 1, 'ADD_TO_CUSTOMS_VALUE', 'AVERTISSEMENT', N'Code des Douanes Algérien Art. 16 octies §1 e) ii)', 3),
    ('EXW', 'FRET_INTERNATIONAL',         N'Fret international',                   1, 1, 'ADD_TO_CUSTOMS_VALUE', 'ERREUR',        N'Code des Douanes Algérien Art. 16 octies §1 e) i)',  4),
    ('EXW', 'ASSURANCE',                  N'Assurance transport international',    1, 1, 'ADD_TO_CUSTOMS_VALUE', 'AVERTISSEMENT', N'Code des Douanes Algérien Art. 16 octies §1 e) i)',  5),
    -- FOB
    ('FOB', 'FRET_INTERNATIONAL',         N'Fret international',                   1, 1, 'ADD_TO_CUSTOMS_VALUE', 'ERREUR',        N'Code des Douanes Algérien Art. 16 octies §1 e) i)',  1),
    ('FOB', 'ASSURANCE',                  N'Assurance transport international',    1, 1, 'ADD_TO_CUSTOMS_VALUE', 'AVERTISSEMENT', N'Code des Douanes Algérien Art. 16 octies §1 e) i)',  2),
    -- CFR
    ('CFR', 'ASSURANCE',                  N'Assurance transport international',    1, 1, 'ADD_TO_CUSTOMS_VALUE', 'AVERTISSEMENT', N'Code des Douanes Algérien Art. 16 octies §1 e) i)',  1);

-- 4. SYNONYMES D'EN-TÊTES EXCEL POUR RECONNAISSANCE AUTOMATIQUE (Section 4)
INSERT INTO [excel].[ColumnHeaderSynonyms] ([CompanyId], [RawHeaderNormalized], [TargetCanonicalField], [ConfidenceScore])
VALUES
    -- Référence produit
    (NULL, N'REF',                 'ProductReference', 100.00),
    (NULL, N'REFERENCE',           'ProductReference', 100.00),
    (NULL, N'ARTICLE',             'ProductReference',  95.00),
    (NULL, N'CODE ARTICLE',        'ProductReference', 100.00),
    (NULL, N'PART NUMBER',         'ProductReference',  95.00),
    (NULL, N'SKU',                 'ProductReference',  95.00),
    -- Désignation
    (NULL, N'DESIGNATION',         'Designation',      100.00),
    (NULL, N'DESIGNATION PRODUIT', 'Designation',      100.00),
    (NULL, N'LIBELLE',             'Designation',       95.00),
    (NULL, N'DESCRIPTION',         'Designation',       95.00),
    -- Quantité
    (NULL, N'QTE',                 'Quantity',         100.00),
    (NULL, N'QUANTITE',            'Quantity',         100.00),
    (NULL, N'QTY',                 'Quantity',         100.00),
    (NULL, N'QUANTITY',            'Quantity',         100.00),
    -- Prix d'achat unitaire
    (NULL, N'PU',                  'UnitPrice',        100.00),
    (NULL, N'PRIX UNIT',           'UnitPrice',        100.00),
    (NULL, N'PRIX UNITAIRE',       'UnitPrice',        100.00),
    (NULL, N'PRIX ACHAT',          'UnitPrice',        100.00),
    (NULL, N'PRIX D ACHAT',        'UnitPrice',        100.00),
    (NULL, N'UNIT PRICE',          'UnitPrice',        100.00),
    -- Devise
    (NULL, N'DEVISE',              'Currency',         100.00),
    (NULL, N'CURRENCY',            'Currency',         100.00),
    (NULL, N'MONNAIE',             'Currency',          95.00),
    -- Code SH / Tarifaire
    (NULL, N'SH',                  'HsCode',           100.00),
    (NULL, N'CODE SH',             'HsCode',           100.00),
    (NULL, N'HS CODE',             'HsCode',           100.00),
    (NULL, N'CODE DOUANE',         'HsCode',           100.00),
    (NULL, N'POSITION TARIFAIRE',  'HsCode',           100.00),
    (NULL, N'SOUS POSITION',       'HsCode',            95.00),
    -- Pays d'origine
    (NULL, N'ORIGINE',             'OriginCountry',    100.00),
    (NULL, N'PAYS D ORIGINE',      'OriginCountry',    100.00),
    (NULL, N'PAYS ORIGINE',        'OriginCountry',    100.00),
    (NULL, N'COUNTRY',             'OriginCountry',     95.00),
    (NULL, N'COUNTRY OF ORIGIN',   'OriginCountry',    100.00),
    -- Droit de douane Excel
    (NULL, N'DD',                  'ExcelDutyRate',    100.00),
    (NULL, N'DROIT DOUANE',        'ExcelDutyRate',    100.00),
    (NULL, N'DROIT DE DOUANE',     'ExcelDutyRate',    100.00),
    (NULL, N'TAUX DD',             'ExcelDutyRate',    100.00),
    -- Incoterm & Poids/Volume
    (NULL, N'INCOTERM',            'Incoterm',         100.00),
    (NULL, N'POIDS',               'GrossWeight',      100.00),
    (NULL, N'POIDS BRUT',          'GrossWeight',      100.00),
    (NULL, N'WEIGHT',              'GrossWeight',       95.00),
    (NULL, N'VOLUME',              'Volume',           100.00);
GO
