using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.Database;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.ExcelEngine;
using ImportCostAlgeria.Presentation.Services;
using ImportCostAlgeria.Presentation.ViewModels;
using ImportCostAlgeria.Presentation.Views;

namespace ImportCostAlgeria.Presentation;

/// <summary>
/// Point d'entrée de l'application Windows CIMP (Priorité 1 du plan de finalisation).
/// Responsabilités exclusives : initialisation de la base (Priorité 2), assemblage des services
/// (composition root), écran de connexion, puis lancement de la fenêtre principale.
/// Aucune règle métier n'est implémentée ici.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Priorité 2 : initialisation propre de la base au premier démarrage (Section "Database").
        InitializeDatabase();

        // Écran de connexion obligatoire (Sections 22 & 32 — rôles utilisateurs).
        var loginViewModel = Services.GetRequiredService<LoginViewModel>();
        var loginWindow = new LoginWindow { DataContext = loginViewModel };

        bool? loginResult = loginWindow.ShowDialog();
        if (loginResult != true || loginViewModel.AuthenticatedUser == null)
        {
            Shutdown();
            return;
        }

        var session = Services.GetRequiredService<SessionContext>();
        session.CurrentUser = loginViewModel.AuthenticatedUser;

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void InitializeDatabase()
    {
        var dbFactory = Services.GetRequiredService<ICimpDbContextFactory>();
        using var ctx = dbFactory.CreateGlobal();
        var (created, initialUser, initialPassword) = DbContextFactory.EnsureDatabaseReadyWithSeed(ctx);

        if (created && initialUser != null && initialPassword != null)
        {
            MessageBox.Show(
                $"Base de données initialisée pour la première fois.\n\n" +
                $"Compte administrateur initial créé :\n" +
                $"  Utilisateur : {initialUser}\n" +
                $"  Mot de passe : {initialPassword}\n\n" +
                $"Merci de le modifier dès que possible depuis l'écran \"Utilisateurs\".",
                "CIMP — Premier démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<ICimpDbContextFactory>(new CimpDbContextFactory());

        // Dépôts (Priorité 2 — couche Database)
        services.AddSingleton<CompanyRepository>();
        services.AddSingleton<ProductRepository>();
        services.AddSingleton<ImportOperationRepository>();
        services.AddSingleton<UserRepository>();
        services.AddSingleton<RegulatoryRuleAdminRepository>();
        services.AddSingleton<ExchangeRateAdminRepository>();
        services.AddSingleton<CalculationSnapshotRepository>();
        services.AddSingleton<DashboardRepository>();

        // Audit (persistant)
        services.AddSingleton<IAuditLogStore>(sp => new EfAuditLogStore(sp.GetRequiredService<ICimpDbContextFactory>()));
        services.AddSingleton<AuditTrailService>();

        // Excel (Priorité 3)
        services.AddSingleton<IMappingTemplateStore>(sp => new EfMappingTemplateStore(sp.GetRequiredService<ICimpDbContextFactory>()));
        services.AddSingleton<ExcelColumnDetectorAndMapper>();
        services.AddSingleton<ProductCatalogService>();
        services.AddSingleton<ExcelImporterService>(sp => new ExcelImporterService(
            sp.GetRequiredService<ExcelColumnDetectorAndMapper>(),
            sp.GetRequiredService<ProductCatalogService>(),
            sp.GetRequiredService<IMappingTemplateStore>()));

        // Moteurs métier (Priorités 4 & 5)
        services.AddSingleton<EngineFactory>();

        // Session & Contexte applicatif
        services.AddSingleton<SessionContext>();

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardScreenViewModel>();
        services.AddSingleton<EntreprisesViewModel>();
        services.AddSingleton<ImportationsViewModel>();
        services.AddTransient<ImportDetailViewModel>();
        services.AddTransient<ExcelImportWizardViewModel>();
        services.AddSingleton<ReglementationViewModel>();
        services.AddSingleton<TauxDeChangeViewModel>();
        services.AddSingleton<AuditLogViewModel>();
        services.AddSingleton<ParametresViewModel>();

        // Fenêtres
        services.AddSingleton<MainWindow>();
    }
}
