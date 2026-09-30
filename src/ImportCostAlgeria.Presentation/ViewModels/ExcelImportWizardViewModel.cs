using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.ExcelEngine;
using ImportCostAlgeria.Presentation.Infrastructure;
using Microsoft.Win32;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>Une ligne de la grille de mapping interactif (Sections 4 & 5).</summary>
public sealed class MappingRowViewModel : ObservableObject
{
    public string ColumnLetter { get; init; } = string.Empty;
    public string RawHeaderText { get; init; } = string.Empty;
    public bool WasAutomaticallyRecognized { get; init; }

    private CanonicalExcelField _matchedField;
    public CanonicalExcelField MatchedField
    {
        get => _matchedField;
        set => SetField(ref _matchedField, value);
    }
}

/// <summary>
/// Assistant d'import Excel/CSV (Priorité 3 — fonctionnalité PRIORITAIRE) : sélection de fichier réel,
/// détection automatique des colonnes, mapping interactif, sauvegarde du mapping par fournisseur,
/// rapprochement avec le catalogue produits, prévisualisation avant import définitif des lignes.
/// </summary>
public sealed class ExcelImportWizardViewModel : ObservableObject
{
    private readonly ExcelImporterService _importer;

    private Company _company = null!;
    private ImportOperation _operation = null!;
    private RawExcelSheetData? _sheet;
    private string _filePath = string.Empty;
    private string _statusMessage = "Sélectionnez un fichier Excel (.xlsx) ou CSV à importer.";
    private bool _readyToImport;
    private bool _saveAsTemplate;
    private string _templateName = string.Empty;

    public ExcelImportWizardViewModel(ExcelImporterService importer)
    {
        _importer = importer;
        MappingRows = new ObservableCollection<MappingRowViewModel>();
        PreviewLines = new ObservableCollection<ImportLine>();
        CatalogMessages = new ObservableCollection<string>();
        ImportedLines = new List<ImportLine>();

        AllFields = Enum.GetValues<CanonicalExcelField>();

        BrowseCommand = new RelayCommand(BrowseFile);
        AnalyzeCommand = new RelayCommand(AnalyzeMapping, () => _sheet != null);
        ConfirmCommand = new RelayCommand(ConfirmImport, () => _sheet != null && MappingRows.Count > 0);
    }

    public void Initialize(Company company, ImportOperation operation)
    {
        _company = company;
        _operation = operation;
        _templateName = $"{operation.SupplierName}";
    }

    public CanonicalExcelField[] AllFields { get; }
    public ObservableCollection<MappingRowViewModel> MappingRows { get; }
    public ObservableCollection<ImportLine> PreviewLines { get; }
    public ObservableCollection<string> CatalogMessages { get; }
    public List<ImportLine> ImportedLines { get; }

    public string FilePath { get => _filePath; private set => SetField(ref _filePath, value); }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public bool ReadyToImport { get => _readyToImport; private set => SetField(ref _readyToImport, value); }

    public bool SaveAsTemplate { get => _saveAsTemplate; set => SetField(ref _saveAsTemplate, value); }
    public string TemplateName { get => _templateName; set => SetField(ref _templateName, value); }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand AnalyzeCommand { get; }
    public RelayCommand ConfirmCommand { get; }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir un fichier fournisseur (Excel ou CSV)",
            Filter = "Fichiers Excel/CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|Tous les fichiers (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            FilePath = dialog.FileName;
            _sheet = ExcelFileReader.ReadFirstSheet(FilePath);
            StatusMessage = $"Fichier chargé : {_sheet.DataRowsByColumnLetter.Count} ligne(s) de données détectée(s) sur {_sheet.Headers.Count} colonne(s). Cliquez sur \"Analyser les colonnes\".";
            MappingRows.Clear();
            PreviewLines.Clear();
            CatalogMessages.Clear();
            ReadyToImport = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur de lecture du fichier : {ex.Message}";
            _sheet = null;
        }
    }

    private void AnalyzeMapping()
    {
        if (_sheet == null) return;

        var detector = new ExcelColumnDetectorAndMapper();
        var analysis = detector.AnalyzeHeaders(_sheet.Headers, Array.Empty<ExcelMappingTemplate>());

        MappingRows.Clear();
        foreach (var col in analysis.Columns)
        {
            MappingRows.Add(new MappingRowViewModel
            {
                ColumnLetter = col.ColumnLetter,
                RawHeaderText = col.RawHeaderText,
                WasAutomaticallyRecognized = col.IsAutomaticallyRecognized,
                MatchedField = col.MatchedField
            });
        }

        int unrecognized = MappingRows.Count(r => r.MatchedField == CanonicalExcelField.Unmapped);
        StatusMessage = unrecognized == 0
            ? "Toutes les colonnes ont été reconnues automatiquement. Vérifiez puis confirmez l'import."
            : $"{unrecognized} colonne(s) non reconnue(s) automatiquement : veuillez préciser leur correspondance ci-dessous avant de confirmer.";
    }

    private void ConfirmImport()
    {
        if (_sheet == null) return;

        var overrides = MappingRows.ToDictionary(r => r.ColumnLetter, r => r.MatchedField);
        var result = _importer.ProcessExcelSheet(
            _company.Id,
            _sheet,
            _operation.MainCurrencyCode,
            _operation.DefaultOriginCountryIso2,
            overrides);

        if (result.RequiresInteractiveUserMapping)
        {
            StatusMessage = "Veuillez attribuer une correspondance à toutes les colonnes (aucune ne doit rester \"Non mappée\").";
            ReadyToImport = false;
            return;
        }

        PreviewLines.Clear();
        foreach (var line in result.ConvertedLines)
            PreviewLines.Add(line);

        CatalogMessages.Clear();
        foreach (var proposal in result.CatalogProposals)
            CatalogMessages.Add($"Référence reconnue au catalogue : {proposal.Reference} — {proposal.RegulatorySafetyNoticeFr}");
        foreach (var msg in result.ValidationMessages)
            CatalogMessages.Add("⚠️ " + msg);

        if (SaveAsTemplate && !string.IsNullOrWhiteSpace(TemplateName))
        {
            _importer.SaveUserMappingAsTemplate(
                _company.Id,
                TemplateName,
                _sheet.Headers.Select(h => h.RawHeader).ToList(),
                overrides);
            CatalogMessages.Add($"Modèle de mapping \"{TemplateName}\" enregistré pour les prochains imports de ce fournisseur.");
        }

        ImportedLines.Clear();
        ImportedLines.AddRange(result.ConvertedLines);
        ReadyToImport = true;
        StatusMessage = $"{result.ConvertedLines.Count} ligne(s) prête(s) à être importée(s). Cliquez sur \"Importer les lignes\" pour terminer.";
    }
}
