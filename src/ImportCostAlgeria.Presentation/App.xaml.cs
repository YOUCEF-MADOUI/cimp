using System;
using System.Linq;
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

    public App()
    {
        // Diagnostic permanent (revue du 2026-10-01, point 4/5) : capte toute exception qui serait
        // autrement silencieusement avalée par WPF et terminerait le processus avec le code 0 sans
        // aucun message (c'est exactement le symptôme rapporté : "le processus se termine avec le code 0").
        // N'affaiblit ni ne contourne aucune règle de sécurité : affiche seulement l'erreur réelle au lieu
        // de la masquer, pour que ce type de panne ne reste plus jamais invisible.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[CIMP][FATAL][UI] " + e.Exception);
        MessageBox.Show(
            $"CIMP a rencontré une erreur inattendue et va s'arrêter :\n\n{e.Exception.GetType().Name} : {e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "CIMP — Erreur inattendue (thread UI)",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[CIMP][FATAL][NonUI] " + e.ExceptionObject);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Diagnostics.Debug.WriteLine("[CIMP][STARTUP] OnStartup : début.");

        // Correction du bug "fermeture silencieuse après connexion" (revue du 2026-10-01) :
        // ShutdownMode vaut OnLastWindowClose par défaut (ni App.xaml ni le code ne le changeaient).
        // LoginWindow est la toute première fenêtre affichée via ShowDialog() ci-dessous ; comme aucun
        // Application.MainWindow n'a encore été assigné explicitement à ce stade, WPF la désigne
        // IMPLICITEMENT comme fenêtre principale. Dès que l'utilisateur s'authentifie avec succès
        // (LoginWindow.DialogResult = true -> fermeture de la fenêtre), WPF détecte "dernière fenêtre
        // fermée" et appelle Application.Shutdown() en INTERNE, de façon SYNCHRONE, AVANT MÊME que
        // ShowDialog() ne redonne la main au code qui suit : ChangePasswordWindow et MainWindow ne
        // s'affichent donc jamais, et le processus se termine avec le code 0, sans aucune exception
        // (c'est exactement le symptôme observé). On neutralise ce comportement automatique tant que la
        // fenêtre principale définitive (MainWindow) n'est pas affichée.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Priorité 2 : initialisation propre de la base au premier démarrage (Section "Database").
        InitializeDatabase();

        // Écran de connexion obligatoire (Sections 22 & 32 — rôles utilisateurs).
        System.Diagnostics.Debug.WriteLine("[CIMP][STARTUP] Affichage de LoginWindow...");
        var loginViewModel = Services.GetRequiredService<LoginViewModel>();
        var loginWindow = new LoginWindow { DataContext = loginViewModel };

        // Section 4.3 (demande utilisateur — "Se souvenir de moi") : tente une reconnexion automatique si
        // un identifiant a été mémorisé précédemment (jamais journalisé en clair : seul le résultat
        // booléen de la tentative apparaît dans les traces de diagnostic, jamais le mot de passe lui-même).
        // Correction 2026-10-02 (demande utilisateur — "Utiliser un autre compte") : un utilisateur qui a
        // explicitement choisi "Changer de compte" depuis le menu principal (voir MainViewModel) relance
        // CIMP avec cet argument pour forcer l'affichage du formulaire de connexion MANUEL cette fois,
        // même si un identifiant reste mémorisé — sans jamais supprimer ce secret mémorisé (l'utilisateur
        // peut simplement revenir à son compte habituel au prochain lancement normal).
        bool forceManualLogin = e.Args.Contains("--switch-account", StringComparer.OrdinalIgnoreCase);

        bool autoLoginSucceeded = false;
        string? rememberedPassword = forceManualLogin ? null : loginViewModel.TryLoadRememberedCredential();
        if (rememberedPassword != null)
        {
            System.Diagnostics.Debug.WriteLine("[CIMP][STARTUP] Identifiant mémorisé détecté, tentative de reconnexion automatique...");
            autoLoginSucceeded = loginViewModel.TryLogin(rememberedPassword);
            System.Diagnostics.Debug.WriteLine(
                $"[CIMP][STARTUP] Reconnexion automatique : {(autoLoginSucceeded ? "réussie" : "échouée (secret mémorisé invalidé)")}.");

            if (!autoLoginSucceeded)
            {
                // Le mot de passe mémorisé n'est plus valide (ex : modifié depuis) : LoginViewModel.TryLogin
                // a déjà invalidé/supprimé l'ancien secret (Section 4.3). On réinitialise la case à cocher
                // pour refléter fidèlement qu'il n'y a plus rien de mémorisé, et on laisse l'utilisateur
                // ressaisir son mot de passe manuellement (le nom d'utilisateur reste préempli).
                loginViewModel.RememberMe = false;
            }
        }

        bool? loginResult = autoLoginSucceeded ? true : loginWindow.ShowDialog();
        System.Diagnostics.Debug.WriteLine(
            $"[CIMP][STARTUP] LoginWindow fermée. loginResult={loginResult}, " +
            $"AuthenticatedUser={(loginViewModel.AuthenticatedUser?.Username ?? "null")}");

        if (loginResult != true || loginViewModel.AuthenticatedUser == null)
        {
            Shutdown();
            return;
        }

        var session = Services.GetRequiredService<SessionContext>();
        session.CurrentUser = loginViewModel.AuthenticatedUser;
        System.Diagnostics.Debug.WriteLine(
            $"[CIMP][STARTUP] session.CurrentUser = {session.CurrentUser.Username}, " +
            $"MustChangePasswordOnNextLogin = {session.CurrentUser.MustChangePasswordOnNextLogin}");

        // Section 18 (sécurité) : changement de mot de passe obligatoire tant que l'utilisateur utilise
        // encore le mot de passe initial généré aléatoirement (voir DbContextFactory.EnsureDatabaseReadyWithSeed).
        if (session.CurrentUser.MustChangePasswordOnNextLogin)
        {
            System.Diagnostics.Debug.WriteLine("[CIMP][STARTUP] Affichage de ChangePasswordWindow (obligatoire)...");
            var userRepository = Services.GetRequiredService<UserRepository>();
            var changePasswordViewModel = new ChangePasswordViewModel(userRepository, session.CurrentUser);
            var changePasswordWindow = new ChangePasswordWindow { DataContext = changePasswordViewModel };

            bool? changePasswordResult = changePasswordWindow.ShowDialog();
            System.Diagnostics.Debug.WriteLine($"[CIMP][STARTUP] ChangePasswordWindow fermée. résultat={changePasswordResult}");

            if (changePasswordResult == true)
            {
                // Section 4.3 : le changement du mot de passe initial invalide tout secret mémorisé.
                Services.GetRequiredService<RememberedLoginStore>().Clear();
            }

            if (changePasswordResult != true)
            {
                Shutdown();
                return;
            }
        }

        System.Diagnostics.Debug.WriteLine("[CIMP][STARTUP] Création de MainWindow...");
        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        // La fenêtre principale définitive est affichée : on restaure un cycle de vie standard où la
        // fermeture de MainWindow entraîne la fermeture normale de l'application.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
        System.Diagnostics.Debug.WriteLine("[CIMP][STARTUP] MainWindow affichée avec succès.");
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
                $"Ce mot de passe est généré aléatoirement et à usage unique : vous devrez le changer " +
                $"immédiatement après cette fenêtre, avant de pouvoir accéder à l'application.",
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

        // Section 4 (demande utilisateur — "Se souvenir de moi") : stockage sécurisé (DPAPI) de
        // l'identifiant mémorisé, local au profil Windows de l'utilisateur courant.
        services.AddSingleton<RememberedLoginStore>();

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
