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

    // Correction (sécurité du premier démarrage) : au tout premier lancement (base vide), un compte
    // administrateur est créé automatiquement AVEC un mot de passe aléatoire fort — mais ce mot de passe
    // n'est plus jamais communiqué à l'utilisateur (ni affiché, ni journalisé) : il ne pourrait de toute
    // façon jamais le ressaisir utilement. _firstLaunchAdminUsername reste null à tout autre démarrage
    // (dès qu'au moins un utilisateur existe déjà en base, voir DbContextFactory.EnsureDatabaseReadyWithSeed),
    // ce qui garantit que le contournement de LoginWindow ci-dessous ne s'applique QUE pour ce tout premier
    // démarrage, jamais ensuite (Section "Ne pas créer un mécanisme permanent de connexion sans mot de passe").
    private string? _firstLaunchAdminUsername;

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

        var session = Services.GetRequiredService<SessionContext>();

        if (_firstLaunchAdminUsername != null)
        {
            // Correction (sécurité du premier démarrage) : la base vient d'être créée à l'instant et le
            // compte administrateur initial vient d'être seedé (voir InitializeDatabase ci-dessous) — on
            // n'affiche PAS LoginWindow et on ne demande AUCUN mot de passe (l'utilisateur ne le connaît de
            // toute façon pas : il n'est plus jamais communiqué, voir InitializeDatabase). La session
            // administrateur est ouverte automatiquement, UNIQUEMENT pour ce tout premier démarrage —
            // MustChangePasswordOnNextLogin vaut déjà "true" pour ce compte (voir
            // DbContextFactory.EnsureDatabaseReadyWithSeed), ce qui déclenche obligatoirement, juste après,
            // le bloc ChangePasswordWindow ci-dessous AVANT tout accès à MainWindow. Ce contournement ne
            // peut PAS se reproduire aux démarrages suivants : dès qu'un utilisateur existe en base,
            // EnsureDatabaseReadyWithSeed ne renseigne plus jamais _firstLaunchAdminUsername (reste null),
            // et le flux normal de LoginWindow (bloc "else" ci-dessous) s'applique systématiquement.
            System.Diagnostics.Debug.WriteLine(
                "[CIMP][STARTUP] Premier démarrage détecté : ouverture automatique de la session administrateur " +
                "(sans mot de passe), sécurisation du compte obligatoire avant tout accès à l'application.");

            var userRepositoryForFirstLaunch = Services.GetRequiredService<UserRepository>();
            session.CurrentUser = userRepositoryForFirstLaunch.FindByUsername(_firstLaunchAdminUsername)
                ?? throw new InvalidOperationException(
                    "Compte administrateur initial introuvable juste après sa création (incohérence base de données).");
        }
        else
        {
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

            session.CurrentUser = loginViewModel.AuthenticatedUser;
        }

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
        var (created, initialUsername, initialPassword) = DbContextFactory.EnsureDatabaseReadyWithSeed(ctx);

        // Correction (sécurité du premier démarrage) : le mot de passe aléatoire généré n'est PLUS JAMAIS
        // affiché à l'utilisateur (ni journalisé) — un utilisateur novice qui ne le recopierait pas se
        // retrouvait auparavant bloqué sans aucun moyen simple de le récupérer. À la place, on mémorise
        // uniquement le NOM d'utilisateur du compte administrateur qui vient d'être créé : OnStartup s'en
        // sert pour ouvrir automatiquement cette session, sans mot de passe, UNIQUEMENT pour ce tout premier
        // démarrage, immédiatement suivi de la fenêtre obligatoire "Sécurisez votre compte administrateur"
        // (ChangePasswordWindow) qui force l'utilisateur à définir lui-même son propre mot de passe avant
        // tout accès à MainWindow. PasswordHasher continue de générer et hasher un mot de passe aléatoire
        // fort en base (jamais un mot de passe vide ou prévisible) tant que ce choix personnel n'a pas été
        // fait, mais ce mot de passe généré n'est plus jamais exploitable/affiché : voir OnStartup.
        if (created && initialUsername != null && initialPassword != null)
        {
            _firstLaunchAdminUsername = initialUsername;
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
