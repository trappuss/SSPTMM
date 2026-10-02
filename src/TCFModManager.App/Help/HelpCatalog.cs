using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Help;

//
// What the Help page holds and in what order: sections, the how-tos in each, and the steps of each
// how-to. The words are in Strings.resx; this file is only the shape.
//
// Every string is a Func rather than a string, read each time it is shown, so the page follows a
// language change the way the rest of the app does.
//
// A step is a format string, and its {0}, {1}... are filled from the label of the control the step
// names - the same key that control reads. That keeps a step's button name identical to the button
// on screen in every language, and after any rename, without a translator having to find it.
//
internal static class HelpCatalog
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new(StartSectionId, () => Strings.Help_Start_Title, SymbolRegular.Rocket24, null,
        [
            Topic("start.setup", () => Strings.Help_Start_Setup_Title,
                Step(() => Strings.Help_Start_Setup_Step1, () => Strings.Nav_Options),
                Step(() => Strings.Help_Start_Setup_Step2,
                    () => Strings.Options_InstallFolderHeader, () => Strings.Options_Browse),
                Step(() => Strings.Help_Start_Setup_Step3, () => Strings.Common_Save))
                .WithNote(() => Strings.Help_Start_Setup_Note)
                .WithKeywords(() => Strings.Help_Start_Setup_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("start.version", () => Strings.Help_Start_Version_Title,
                Step(() => Strings.Help_Start_Version_Step1),
                Step(() => Strings.Help_Start_Version_Step2,
                    () => Strings.Nav_Options, () => Strings.Options_InstallFolderHeader),
                Step(() => Strings.Help_Start_Version_Step3, () => Strings.Common_Save))
                .WithKeywords(() => Strings.Help_Start_Version_Keywords)
                .WithPage(typeof(OptionsPage)),
        ]),

        new("play", () => Strings.Nav_Play, SymbolRegular.Play24, typeof(PlayPage),
        [
            Topic("play.start", () => Strings.Help_Play_Start_Title,
                Step(() => Strings.Help_Play_Start_Step1, () => Strings.Nav_Play),
                Step(() => Strings.Help_Play_Start_Step2,
                    () => Strings.Play_ServerHeader, () => Strings.Play_StartServer),
                Step(() => Strings.Help_Play_Start_Step3, () => Strings.Play_OpenLauncher))
                .WithNote(() => Strings.Help_Play_Start_Note)
                .WithKeywords(() => Strings.Help_Play_Start_Keywords),

            Topic("play.check", () => Strings.Help_Play_Check_Title,
                Step(() => Strings.Help_Play_Check_Step1, () => Strings.Nav_Play),
                Step(() => Strings.Help_Play_Check_Step2, () => Strings.Play_CheckAgain),
                Step(() => Strings.Help_Play_Check_Step3,
                    () => Strings.Common_ReviewAndInstall, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_Play_Check_Note)
                .WithKeywords(() => Strings.Help_Play_Check_Keywords),
        ]),

        new("browse", () => Strings.Nav_Browse, SymbolRegular.Apps24, typeof(BrowsePage),
        [
            Topic("browse.compatible", () => Strings.Help_Browse_Compatible_Title,
                Step(() => Strings.Help_Browse_Compatible_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Compatible_Step2),
                Step(() => Strings.Help_Browse_Compatible_Step3, () => Strings.Common_ClearFilters))
                .WithKeywords(() => Strings.Help_Browse_Compatible_Keywords),

            Topic("browse.install",
                ByMode(() => Strings.Help_Browse_Install_Title, () => Strings.Help_Browse_Install_Title_Monitor),
                Step(() => Strings.Help_Browse_Install_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Install_Step2,
                    () => Strings.ReadModPage_ButtonOpen, () => Strings.Common_Continue),
                Step(() => Strings.Help_Browse_Install_Step3, () => Strings.Common_Continue),
                Step(ByMode(() => Strings.Help_Browse_Install_Step4, () => Strings.Help_Browse_Install_Step4_Monitor),
                    () => Strings.Nav_Downloads))
                .WithNote(ByMode(() => Strings.Help_Browse_Install_Note, () => Strings.Help_Browse_Install_Note_Monitor))
                .WithKeywords(() => Strings.Help_Browse_Install_Keywords),

            Topic("browse.author", () => Strings.Help_Browse_Author_Title,
                Step(() => Strings.Help_Browse_Author_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Author_Step2))
                .WithKeywords(() => Strings.Help_Browse_Author_Keywords),

            Topic("browse.hideinstalled", () => Strings.Help_Browse_HideInstalled_Title,
                Step(() => Strings.Help_Browse_HideInstalled_Step1,
                    () => Strings.Nav_Browse, () => Strings.Filter_AnyMod, () => Strings.Filter_HideInstalled),
                Step(() => Strings.Help_Browse_HideInstalled_Step2, () => Strings.Common_SaveAsDefault))
                .WithNote(() => Strings.Help_Browse_HideInstalled_Note)
                .WithKeywords(() => Strings.Help_Browse_HideInstalled_Keywords),

            Topic("browse.addon", () => Strings.Help_Browse_Addon_Title,
                Step(() => Strings.Help_Browse_Addon_Step1, () => Strings.Nav_Browse),
                Step(() => Strings.Help_Browse_Addon_Step2),
                Step(() => Strings.Help_Browse_Addon_Step3))
                .WithNote(() => Strings.Help_Browse_Addon_Note)
                .WithKeywords(() => Strings.Help_Browse_Addon_Keywords),
        ]),

        new("installed", () => Strings.Nav_Installed, SymbolRegular.CheckmarkCircle24, typeof(InstalledPage),
        [
            Topic("installed.updates", () => Strings.Help_Installed_Updates_Title,
                Step(() => Strings.Help_Installed_Updates_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Installed_Updates_Step2,
                    () => Strings.Filter_UpdateAny, () => Strings.Filter_UpdateNeeded))
                .WithKeywords(() => Strings.Help_Installed_Updates_Keywords),

            Topic("installed.update", () => Strings.Help_Installed_Update_Title,
                Step(() => Strings.Help_Installed_Update_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_DetailsAndVersions),
                Step(ByMode(() => Strings.Help_Installed_Update_Step2, () => Strings.Help_Installed_Update_Step2_Monitor),
                    () => Strings.ModUpdate_Update, () => Strings.ModUpdate_Redownload),
                Step(() => Strings.Help_Installed_Update_Step3, () => Strings.Common_Continue))
                .WithNote(() => Strings.Help_Installed_Update_Note,
                    () => Strings.Installed_MultiSelect, () => Strings.Installed_UpdateSelected)
                .WithKeywords(() => Strings.Help_Installed_Update_Keywords),

            Topic("installed.views", () => Strings.Help_Installed_Views_Title,
                Step(() => Strings.Help_Installed_Views_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_ViewCards,
                    () => Strings.Installed_ViewGroups, () => Strings.Installed_ViewList),
                Step(() => Strings.Help_Installed_Views_Step2))
                .WithKeywords(() => Strings.Help_Installed_Views_Keywords),

            Topic("installed.groups", () => Strings.Help_Installed_Groups_Title,
                Step(() => Strings.Help_Installed_Groups_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_ViewGroups),
                Step(() => Strings.Help_Installed_Groups_Step2,
                    () => Strings.Installed_NewGroupPlaceholder, () => Strings.Installed_AddGroup),
                Step(() => Strings.Help_Installed_Groups_Step3))
                .WithNote(() => Strings.Help_Installed_Groups_Note)
                .WithKeywords(() => Strings.Help_Installed_Groups_Keywords),

            Topic("installed.disable", () => Strings.Help_Installed_Disable_Title,
                Step(() => Strings.Help_Installed_Disable_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Installed_Disable_Step2),
                Step(() => Strings.Help_Installed_Disable_Step3))
                .WithNote(() => Strings.Help_Installed_Disable_Note)
                .WithKeywords(() => Strings.Help_Installed_Disable_Keywords),

            Topic("installed.disablemany", () => Strings.Help_Installed_DisableMany_Title,
                Step(() => Strings.Help_Installed_DisableMany_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_MultiSelect),
                Step(() => Strings.Help_Installed_DisableMany_Step2, () => Strings.Installed_DisableSelected))
                .WithNote(() => Strings.Help_Installed_DisableMany_Note, () => Strings.Installed_ViewGroups)
                .WithKeywords(() => Strings.Help_Installed_DisableMany_Keywords),

            Topic("installed.undo", () => Strings.Help_Installed_Undo_Title,
                Step(() => Strings.Help_Installed_Undo_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_Undo),
                Step(() => Strings.Help_Installed_Undo_Step2))
                .WithKeywords(() => Strings.Help_Installed_Undo_Keywords),

            Topic("installed.remove", () => Strings.Help_Installed_Remove_Title,
                Step(() => Strings.Help_Installed_Remove_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_Remove),
                Step(() => Strings.Help_Installed_Remove_Step2))
                .WithNote(() => Strings.Help_Installed_Remove_Note)
                .WithKeywords(() => Strings.Help_Installed_Remove_Keywords),

            Topic("installed.downgrade", () => Strings.Help_Installed_Downgrade_Title,
                Step(() => Strings.Help_Installed_Downgrade_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_DetailsAndVersions),
                Step(() => Strings.Help_Installed_Downgrade_Step2, () => Strings.ModUpdate_ShowAll),
                Step(() => Strings.Help_Installed_Downgrade_Step3, () => Strings.ModUpdate_Downgrade))
                .WithNote(() => Strings.Help_Installed_Downgrade_Note)
                .WithKeywords(() => Strings.Help_Installed_Downgrade_Keywords),

            Topic("installed.sortout", () => Strings.Help_Installed_SortOut_Title,
                Step(() => Strings.Help_Installed_SortOut_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Installed_SortOut_Step2, () => Strings.Installed_SortOut))
                .WithNote(() => Strings.Help_Installed_SortOut_Note)
                .WithKeywords(() => Strings.Help_Installed_SortOut_Keywords),
        ]),

        new("modlists", () => Strings.Nav_ModLists, SymbolRegular.AppsList24, typeof(ModListsPage),
        [
            Topic("modlists.capture", () => Strings.Help_ModLists_Capture_Title,
                Step(() => Strings.Help_ModLists_Capture_Step1, () => Strings.Nav_ModLists),
                Step(() => Strings.Help_ModLists_Capture_Step2,
                    () => Strings.ModLists_CaptureHeader, () => Strings.ModLists_Capture))
                .WithNote(() => Strings.Help_ModLists_Capture_Note)
                .WithKeywords(() => Strings.Help_ModLists_Capture_Keywords),

            Topic("modlists.apply", () => Strings.Help_ModLists_Apply_Title,
                Step(() => Strings.Help_ModLists_Apply_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_SavedHeader),
                Step(() => Strings.Help_ModLists_Apply_Step2, () => Strings.ModLists_Preview),
                Step(() => Strings.Help_ModLists_Apply_Step3, () => Strings.ModLists_Apply))
                .WithNote(ByMode(() => Strings.Help_ModLists_Apply_Note, () => Strings.Help_ModLists_Apply_Note_Monitor))
                .WithKeywords(() => Strings.Help_ModLists_Apply_Keywords),

            Topic("modlists.pin", () => Strings.Help_ModLists_Pin_Title,
                Step(() => Strings.Help_ModLists_Pin_Step1,
                    () => Strings.Nav_Installed, () => Strings.Installed_Pin),
                Step(() => Strings.Help_ModLists_Pin_Step2, () => Strings.Installed_Unpin))
                .WithKeywords(() => Strings.Help_ModLists_Pin_Keywords)
                .WithPage(typeof(InstalledPage)),

            Topic("modlists.edit", () => Strings.Help_ModLists_Edit_Title,
                Step(() => Strings.Help_ModLists_Edit_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_AddMods),
                Step(() => Strings.Help_ModLists_Edit_Step2, () => Strings.Common_Save),
                Step(() => Strings.Help_ModLists_Edit_Step3, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Edit_Note, () => Strings.ModLists_MakeCopy)
                .WithKeywords(() => Strings.Help_ModLists_Edit_Keywords),

            Topic("modlists.share", () => Strings.Help_ModLists_Share_Title,
                Step(() => Strings.Help_ModLists_Share_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_Export),
                Step(() => Strings.Help_ModLists_Share_Step2,
                    () => Strings.ModLists_Import, () => Strings.Nav_ModLists),
                Step(() => Strings.Help_ModLists_Share_Step3,
                    () => Strings.ModLists_Preview, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Share_Note)
                .WithKeywords(() => Strings.Help_ModLists_Share_Keywords),

            Topic("modlists.server", () => Strings.Help_ModLists_Server_Title,
                Step(() => Strings.Help_ModLists_Server_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionPages,
                    () => Strings.Options_ServerMapHeader),
                Step(() => Strings.Help_ModLists_Server_Step2,
                    () => Strings.Options_ServerMapConnectionHeader, () => Strings.Options_Connect),
                Step(() => Strings.Help_ModLists_Server_Step3,
                    () => Strings.Nav_ServerMap, () => Strings.Common_ReviewAndInstall),
                Step(() => Strings.Help_ModLists_Server_Step4, () => Strings.ModLists_Apply))
                .WithNote(() => Strings.Help_ModLists_Server_Note)
                .WithKeywords(() => Strings.Help_ModLists_Server_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("modlists.undo", () => Strings.Help_ModLists_Undo_Title,
                Step(() => Strings.Help_ModLists_Undo_Step1,
                    () => Strings.Nav_ModLists,
                    () => LocalizationService.Text(Strings.ModLists_UndoLabelFormat, "…")),
                Step(() => Strings.Help_ModLists_Undo_Step2))
                .WithKeywords(() => Strings.Help_ModLists_Undo_Keywords),
        ]),
        new("configs", () => Strings.Nav_Configs, SymbolRegular.EditSettings24, typeof(ConfigsPage),
        [
            Topic("configs.edit", () => Strings.Help_Configs_Edit_Title,
                Step(() => Strings.Help_Configs_Edit_Step1, () => Strings.Nav_Configs),
                Step(() => Strings.Help_Configs_Edit_Step2),
                Step(() => Strings.Help_Configs_Edit_Step3, () => Strings.Common_Save))
                .WithNote(() => Strings.Help_Configs_Edit_Note, () => Strings.Configs_Revert)
                .WithKeywords(() => Strings.Help_Configs_Edit_Keywords),

            Topic("configs.policy", () => Strings.Help_Configs_Policy_Title,
                Step(() => Strings.Help_Configs_Policy_Step1, () => Strings.Nav_Configs),
                Step(() => Strings.Help_Configs_Policy_Step2,
                    () => Strings.Configs_UpdatePolicyLabel, () => Strings.ConfigPolicy_Merge,
                    () => Strings.ConfigPolicy_KeepMine, () => Strings.ConfigPolicy_TakeNew))
                .WithNote(() => Strings.Help_Configs_Policy_Note)
                .WithKeywords(() => Strings.Help_Configs_Policy_Keywords),

            Topic("configs.original", () => Strings.Help_Configs_Original_Title,
                Step(() => Strings.Help_Configs_Original_Step1,
                    () => Strings.Nav_Configs, () => Strings.Configs_TagShippedDefault),
                Step(() => Strings.Help_Configs_Original_Step2, () => Strings.Common_Save))
                .WithNote(() => Strings.Help_Configs_Original_Note)
                .WithKeywords(() => Strings.Help_Configs_Original_Keywords),

            Topic("configs.leftover", () => Strings.Help_Configs_Leftover_Title,
                Step(() => Strings.Help_Configs_Leftover_Step1,
                    () => Strings.Nav_Configs, () => Strings.Filter_ConfigOther),
                Step(() => Strings.Help_Configs_Leftover_Step2, () => Strings.Configs_SectionUnclaimed))
                .WithNote(() => Strings.Help_Configs_Leftover_Note)
                .WithKeywords(() => Strings.Help_Configs_Leftover_Keywords),
        ]),

        new("dependencies", () => Strings.Nav_Dependencies, SymbolRegular.Branch24, typeof(DependenciesPage),
        [
            Topic("dependencies.conflicts", () => Strings.Help_Dependencies_Conflicts_Title,
                Step(() => Strings.Help_Dependencies_Conflicts_Step1,
                    () => Strings.Nav_Dependencies, () => Strings.Conflicts_Header),
                Step(() => Strings.Help_Dependencies_Conflicts_Step2,
                    () => Strings.Conflicts_KindDuplicatePlugin, () => Strings.Conflicts_KindDuplicateServerMod,
                    () => Strings.Conflicts_KindDifferentCopies),
                Step(() => Strings.Help_Dependencies_Conflicts_Step3, () => Strings.Dependencies_Refresh))
                .WithNote(() => Strings.Help_Dependencies_Conflicts_Note)
                .WithKeywords(() => Strings.Help_Dependencies_Conflicts_Keywords),

            Topic("dependencies.keep", () => Strings.Help_Dependencies_Keep_Title,
                Step(() => Strings.Help_Dependencies_Keep_Step1,
                    () => Strings.Conflicts_Header, () => Strings.Conflicts_KeepThis),
                Step(() => Strings.Help_Dependencies_Keep_Step2),
                Step(() => Strings.Help_Dependencies_Keep_Step3, () => Strings.Nav_Installed))
                .WithNote(() => Strings.Help_Dependencies_Keep_Note, () => Strings.Conflicts_KeepThis)
                .WithKeywords(() => Strings.Help_Dependencies_Keep_Keywords),

            Topic("dependencies.spot", () => Strings.Help_Dependencies_Spot_Title,
                Step(() => Strings.Help_Dependencies_Spot_Step1,
                    () => Strings.Nav_Installed, () => Strings.Nav_Dependencies),
                Step(() => Strings.Help_Dependencies_Spot_Step2, () => Strings.Filter_HasConflicts),
                Step(() => Strings.Help_Dependencies_Spot_Step3, () => Strings.Nav_Play))
                .WithKeywords(() => Strings.Help_Dependencies_Spot_Keywords)
                .WithPage(typeof(InstalledPage)),

            Topic("dependencies.check", () => Strings.Help_Dependencies_Check_Title,
                Step(() => Strings.Help_Dependencies_Check_Step1,
                    () => Strings.Nav_Dependencies, () => Strings.Dependencies_Header),
                Step(() => Strings.Help_Dependencies_Check_Step2),
                Step(() => Strings.Help_Dependencies_Check_Step3, () => Strings.Dependencies_Refresh))
                .WithKeywords(() => Strings.Help_Dependencies_Check_Keywords),
        ]),

        new("footprint", () => Strings.Nav_Footprint, SymbolRegular.Scales24, typeof(FootprintPage),
        [
            Topic("footprint.enable", () => Strings.Help_Footprint_Enable_Title,
                Step(() => Strings.Help_Footprint_Enable_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionPages,
                    () => Strings.Options_FootprintHeader),
                Step(() => Strings.Help_Footprint_Enable_Step2, () => Strings.Nav_Footprint))
                .WithKeywords(() => Strings.Help_Footprint_Enable_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("footprint.read", () => Strings.Help_Footprint_Read_Title,
                Step(() => Strings.Help_Footprint_Read_Step1, () => Strings.Nav_Footprint),
                Step(() => Strings.Help_Footprint_Read_Step2, () => Strings.Footprint_SortByLabel),
                Step(() => Strings.Help_Footprint_Read_Step3))
                .WithNote(() => Strings.Help_Footprint_Read_Note)
                .WithKeywords(() => Strings.Help_Footprint_Read_Keywords),
        ]),

        new("servermap", () => Strings.Nav_ServerMap, SymbolRegular.ServerSurfaceMultiple16, typeof(ServerMapPage),
        [
            Topic("servermap.enable", () => Strings.Help_ServerMap_Enable_Title,
                Step(() => Strings.Help_ServerMap_Enable_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionPages,
                    () => Strings.Options_ServerMapHeader),
                Step(() => Strings.Help_ServerMap_Enable_Step2, () => Strings.Nav_ServerMap))
                .WithNote(() => Strings.Help_ServerMap_Enable_Note, () => Strings.Options_GetServerMapMod)
                .WithKeywords(() => Strings.Help_ServerMap_Enable_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("servermap.connect", () => Strings.Help_ServerMap_Connect_Title,
                Step(() => Strings.Help_ServerMap_Connect_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_ServerMapConnectionHeader),
                Step(() => Strings.Help_ServerMap_Connect_Step2, () => Strings.Options_Connect),
                Step(() => Strings.Help_ServerMap_Connect_Step3,
                    () => Strings.ServerMap_ConsentAllow, () => Strings.ServerMap_ConsentDecline),
                Step(() => Strings.Help_ServerMap_Connect_Step4,
                    () => Strings.Nav_ServerMap, () => Strings.Nav_ModLists))
                .WithNote(() => Strings.Help_ServerMap_Connect_Note, () => Strings.Options_TrustCertificate)
                .WithKeywords(() => Strings.Help_ServerMap_Connect_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("servermap.publish", () => Strings.Help_ServerMap_Publish_Title,
                Step(() => Strings.Help_ServerMap_Publish_Step1,
                    () => Strings.Nav_ModLists, () => Strings.ModLists_Capture),
                Step(() => Strings.Help_ServerMap_Publish_Step2),
                Step(() => Strings.Help_ServerMap_Publish_Step3,
                    () => Strings.Common_Save, () => Strings.ModLists_Publish))
                .WithNote(() => Strings.Help_ServerMap_Publish_Note)
                .WithKeywords(() => Strings.Help_ServerMap_Publish_Keywords)
                .WithPage(typeof(ModListsPage)),
        ]),

        new("downloads", () => Strings.Nav_Downloads, SymbolRegular.ArrowDownload24, typeof(DownloadsPage),
        [
            Topic("downloads.watch", () => Strings.Help_Downloads_Watch_Title,
                Step(ByMode(() => Strings.Help_Downloads_Watch_Step1, () => Strings.Help_Downloads_Watch_Step1_Monitor),
                    () => Strings.Nav_Downloads),
                Step(() => Strings.Help_Downloads_Watch_Step2, () => Strings.Common_Cancel),
                Step(() => Strings.Help_Downloads_Watch_Step3, () => Strings.Downloads_RetryFailed))
                .WithNote(ByMode(() => Strings.Help_Downloads_Watch_Note, () => Strings.Help_Downloads_Watch_Note_Monitor))
                .WithKeywords(() => Strings.Help_Downloads_Watch_Keywords),

            Topic("downloads.where", () => Strings.Help_Downloads_Where_Title,
                Step(() => Strings.Help_Downloads_Where_Step1, () => Strings.Nav_Downloads),
                Step(() => Strings.Help_Downloads_Where_Step2, () => Strings.Downloads_ShowInFolder))
                .WithKeywords(() => Strings.Help_Downloads_Where_Keywords),
        ]),

        new("monitor", () => Strings.Help_Monitor_Title, SymbolRegular.DocumentSave24, null,
        [
            Topic("monitor.enable", () => Strings.Help_Monitor_Enable_Title,
                Step(() => Strings.Help_Monitor_Enable_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionInstalling),
                Step(() => Strings.Help_Monitor_Enable_Step2,
                    () => Strings.Options_InstallModeHeader, () => Strings.Options_MonitorModeDownloadOnly),
                Step(() => Strings.Help_Monitor_Enable_Step3, () => Strings.Options_MonitorFolderHeader))
                .WithNote(() => Strings.Help_Monitor_Enable_Note)
                .WithKeywords(() => Strings.Help_Monitor_Enable_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("monitor.other", () => Strings.Help_Monitor_Other_Title,
                Step(() => Strings.Help_Monitor_Other_Step1),
                Step(() => Strings.Help_Monitor_Other_Step2))
                .WithKeywords(() => Strings.Help_Monitor_Other_Keywords)
                .WithPage(typeof(BrowsePage)),

            Topic("monitor.confirm", () => Strings.Help_Monitor_Confirm_Title,
                Step(() => Strings.Help_Monitor_Confirm_Step1),
                Step(() => Strings.Help_Monitor_Confirm_Step2,
                    () => Strings.Nav_Installed, () => Strings.DownloadConfirm_Confirm),
                Step(() => Strings.Help_Monitor_Confirm_Step3, () => Strings.Installed_ConfirmDownload))
                .WithNote(() => Strings.Help_Monitor_Confirm_Note, () => Strings.Options_MonitorConfirmHeader)
                .WithKeywords(() => Strings.Help_Monitor_Confirm_Keywords)
                .WithPage(typeof(InstalledPage)),
        ]),

        new("updates", () => Strings.Options_UpdateNotificationsHeader, SymbolRegular.Alert24, null,
        [
            Topic("updates.enable", () => Strings.Help_Updates_Enable_Title,
                Step(() => Strings.Help_Updates_Enable_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionUpdates,
                    () => Strings.Options_UpdateNotificationsHeader),
                Step(() => Strings.Help_Updates_Enable_Step2, () => Strings.Options_UpdateIntervalHeader),
                Step(() => Strings.Help_Updates_Enable_Step3, () => Strings.Options_UpdateCheckNow))
                .WithNote(() => Strings.Help_Updates_Enable_Note, () => Strings.Nav_Installed)
                .WithKeywords(() => Strings.Help_Updates_Enable_Keywords)
                .WithPage(typeof(OptionsPage)),

            Topic("updates.tray", () => Strings.Help_Updates_Tray_Title,
                Step(() => Strings.Help_Updates_Tray_Step1,
                    () => Strings.Options_UpdateNotificationsHeader, () => Strings.Options_TrayOff),
                Step(() => Strings.Help_Updates_Tray_Step2),
                Step(() => Strings.Help_Updates_Tray_Step3, () => Strings.Tray_Quit))
                .WithKeywords(() => Strings.Help_Updates_Tray_Keywords)
                .WithPage(typeof(OptionsPage)),
        ]),

        new("appupdate", () => Strings.Nav_AppUpdate, SymbolRegular.ArrowCircleUp24, typeof(AppUpdatePage),
        [
            Topic("appupdate.update", () => Strings.Help_AppUpdate_Update_Title,
                Step(() => Strings.Help_AppUpdate_Update_Step1,
                    () => Strings.MainWindow_SeeWhatsNew, () => Strings.Nav_AppUpdate),
                Step(() => Strings.Help_AppUpdate_Update_Step2, () => Strings.AppUpdate_DownloadAndInstall),
                Step(() => Strings.Help_AppUpdate_Update_Step3))
                .WithNote(() => Strings.Help_AppUpdate_Update_Note, () => Strings.AppUpdate_CheckNow)
                .WithKeywords(() => Strings.Help_AppUpdate_Update_Keywords),
        ]),

        new("options", () => Strings.Nav_Options, SymbolRegular.Settings24, typeof(OptionsPage),
        [
            Topic("options.language", () => Strings.Help_Options_Language_Title,
                Step(() => Strings.Help_Options_Language_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionGeneral,
                    () => Strings.Options_Language_Header),
                Step(() => Strings.Help_Options_Language_Step2))
                .WithNote(() => Strings.Help_Options_Language_Note, () => Strings.Help_ReportProblem)
                .WithKeywords(() => Strings.Help_Options_Language_Keywords),

            Topic("options.theme", () => Strings.Help_Options_Theme_Title,
                Step(() => Strings.Help_Options_Theme_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_AppearanceHeader,
                    () => Strings.Options_ThemeFollowSystem, () => Strings.Options_ThemeLight,
                    () => Strings.Options_ThemeDark))
                .WithKeywords(() => Strings.Help_Options_Theme_Keywords),

            Topic("options.role", () => Strings.Help_Options_Role_Title,
                Step(() => Strings.Help_Options_Role_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_SectionMultiplayer,
                    () => Strings.Options_MachineRoleHeader),
                Step(() => Strings.Help_Options_Role_Step2,
                    () => Strings.Options_PlaysHereOff, () => Strings.Options_HeadlessOn))
                .WithNote(() => Strings.Help_Options_Role_Note, () => Strings.Options_HeadlessLauncherHeader)
                .WithKeywords(() => Strings.Help_Options_Role_Keywords),
        ]),

        new(ProblemsSectionId, () => Strings.Help_Problems_Title, SymbolRegular.Wrench24, null,
        [
            Topic("problems.notloading", () => Strings.Help_Problems_NotLoading_Title,
                Step(() => Strings.Help_Problems_NotLoading_Step1,
                    () => Strings.Nav_Dependencies, () => Strings.Conflicts_Header),
                Step(() => Strings.Help_Problems_NotLoading_Step2, () => Strings.Dependencies_Header),
                Step(() => Strings.Help_Problems_NotLoading_Step3, () => Strings.Nav_Installed))
                .WithNote(() => Strings.Help_Problems_NotLoading_Note, () => Strings.Nav_Browse)
                .WithKeywords(() => Strings.Help_Problems_NotLoading_Keywords)
                .WithPage(typeof(DependenciesPage)),

            Topic("problems.greyed", () => Strings.Help_Problems_Greyed_Title,
                Step(() => Strings.Help_Problems_Greyed_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Problems_Greyed_Step2))
                .WithPage(typeof(InstalledPage))
                .WithKeywords(() => Strings.Help_Problems_Greyed_Keywords),

            Topic("problems.closespt", () => Strings.Help_Problems_CloseSpt_Title,
                Step(() => Strings.Help_Problems_CloseSpt_Step1),
                Step(() => Strings.Help_Problems_CloseSpt_Step2))
                .WithNote(() => Strings.Help_Problems_CloseSpt_Note)
                .WithKeywords(() => Strings.Help_Problems_CloseSpt_Keywords),

            Topic("problems.partial", () => Strings.Help_Problems_Partial_Title,
                Step(() => Strings.Help_Problems_Partial_Step1, () => Strings.Nav_Installed),
                Step(() => Strings.Help_Problems_Partial_Step2,
                    () => Strings.Installed_DetailsAndVersions, () => Strings.ModUpdate_Redownload,
                    () => Strings.Installed_Remove))
                .WithNote(() => Strings.Help_Problems_Partial_Note)
                .WithPage(typeof(InstalledPage))
                .WithKeywords(() => Strings.Help_Problems_Partial_Keywords),

            Topic("problems.notifications", () => Strings.Help_Problems_Notifications_Title,
                Step(() => Strings.Help_Problems_Notifications_Step1,
                    () => Strings.Nav_Options, () => Strings.Options_UpdateNotificationsHeader,
                    () => Strings.Options_UpdateCheckNow),
                Step(() => Strings.Help_Problems_Notifications_Step2),
                Step(() => Strings.Help_Problems_Notifications_Step3))
                .WithPage(typeof(OptionsPage))
                .WithKeywords(() => Strings.Help_Problems_Notifications_Keywords),

            Topic("problems.appupdate", () => Strings.Help_Problems_AppUpdate_Title,
                Step(() => Strings.Help_Problems_AppUpdate_Step1),
                Step(() => Strings.Help_Problems_AppUpdate_Step2, () => Strings.Nav_AppUpdate))
                .WithNote(() => Strings.Help_Problems_AppUpdate_Note)
                .WithPage(typeof(AppUpdatePage))
                .WithKeywords(() => Strings.Help_Problems_AppUpdate_Keywords),

            Topic("problems.report", () => Strings.Help_Problems_Report_Title,
                Step(() => Strings.Help_Problems_Report_Step1, () => Strings.Help_ReportProblem),
                Step(() => Strings.Help_Problems_Report_Step2),
                Step(() => Strings.Help_Problems_Report_Step3))
                .WithNote(() => Strings.Help_Problems_Report_Note)
                .WithKeywords(() => Strings.Help_Problems_Report_Keywords),
        ]),
    ];

    public const string StartSectionId = "start";

    public const string ProblemsSectionId = "problems";

    // The app's issues tab on sp-mod.com - where Report a problem goes.
    public static string IssuesUrl => SelfMod.ModPageUrl + "#issues";

    // The section the "?" opens for a page: its own, or Getting started for a page that has none.
    public static string SectionIdFor(Type? pageType) =>
        Sections.FirstOrDefault(s => pageType is not null && s.PageType == pageType)?.Id ?? StartSectionId;

    //
    // A string that reads differently in Monitor mode (R6), so an install step describes the buttons
    // the reader is actually looking at. Both forms are handed the same labels, so they must use the
    // same placeholders. Read on every get - the Help view models are refreshed when the mode changes.
    //
    private static Func<string> ByMode(Func<string> install, Func<string> downloadOnly) =>
        () => AppServices.ModPageGate.IsDownloadOnly ? downloadOnly() : install();

    // The sidebar name of a page Open can go to, so the button reads "Open Installed" in any language.
    public static string PageName(Type page) => page.Name switch
    {
        nameof(PlayPage) => Strings.Nav_Play,
        nameof(BrowsePage) => Strings.Nav_Browse,
        nameof(InstalledPage) => Strings.Nav_Installed,
        nameof(ModListsPage) => Strings.Nav_ModLists,
        nameof(ConfigsPage) => Strings.Nav_Configs,
        nameof(DependenciesPage) => Strings.Nav_Dependencies,
        nameof(FootprintPage) => Strings.Nav_Footprint,
        nameof(ServerMapPage) => Strings.Nav_ServerMap,
        nameof(DownloadsPage) => Strings.Nav_Downloads,
        nameof(AppUpdatePage) => Strings.Nav_AppUpdate,
        nameof(OptionsPage) => Strings.Nav_Options,
        _ => page.Name,
    };

    // Whether a page can be opened right now. The two optional pages are out of the sidebar until
    // switched on, and sending someone to a page they can't see from the sidebar would only confuse -
    // their "Turn this page on" how-to goes to Options instead.
    public static bool CanOpen(Type page) =>
        page != typeof(FootprintPage) && page != typeof(ServerMapPage)
        || (page == typeof(FootprintPage) ? AppServices.FootprintGate.IsPageEnabled : AppServices.ServerMap.IsPageEnabled);

    private static HelpTopic Topic(string id, Func<string> title, params HelpStep[] steps) =>
        new(id, title, steps);

    private static HelpStep Step(Func<string> format, params Func<string>[] labels) =>
        new(format, labels);
}

// One sidebar page's worth of how-tos, or Getting started. PageType is the page the "?" opens this
// section from; null for a section that isn't a page.
internal sealed record HelpSection(
    string Id,
    Func<string> Title,
    SymbolRegular Icon,
    Type? PageType,
    IReadOnlyList<HelpTopic> Topics);

internal sealed record HelpTopic(string Id, Func<string> Title, IReadOnlyList<HelpStep> Steps)
{
    public HelpStep? Note { get; private init; }

    // Other words people type for this - "uninstall" for Remove. Searched with the title, never shown.
    public Func<string>? Keywords { get; private init; }

    // The page Open takes the reader to, when it isn't the section's own. Null with PageSet means none.
    public Type? Page { get; private init; }

    public bool PageSet { get; private init; }

    public HelpTopic WithNote(Func<string> format, params Func<string>[] labels) =>
        this with { Note = new HelpStep(format, labels) };

    public HelpTopic WithKeywords(Func<string> keywords) => this with { Keywords = keywords };

    public HelpTopic WithPage(Type? page) => this with { Page = page, PageSet = true };
}

internal sealed record HelpStep(Func<string> Format, IReadOnlyList<Func<string>> Labels);

// The pieces a step renders as: plain text, and the control labels it names in bold.
internal readonly record struct HelpRun(string Text, bool IsLabel);

internal static class HelpText
{
    //
    // Splits "Press {0}, then {1}." into runs, the placeholders replaced by their labels.
    //
    // Done by hand rather than with string.Format so the labels can come out bold, and so a
    // translation whose placeholders don't match what the catalog passes shows the text it has
    // instead of throwing. The test that every translation keeps English's placeholders is what
    // stops that happening; this only keeps it from taking the page down if it ever does.
    //
    public static IReadOnlyList<HelpRun> Runs(HelpStep step)
    {
        var format = step.Format();
        var runs = new List<HelpRun>();
        var text = new System.Text.StringBuilder();
        var i = 0;

        while (i < format.Length)
        {
            var c = format[i];

            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{')
            {
                text.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                text.Append('}');
                i += 2;
                continue;
            }

            if (c == '{')
            {
                var close = format.IndexOf('}', i);
                if (close > i
                    && int.TryParse(format.AsSpan(i + 1, close - i - 1), out var index)
                    && index >= 0 && index < step.Labels.Count)
                {
                    if (text.Length > 0) runs.Add(new HelpRun(text.ToString(), false));
                    text.Clear();
                    runs.Add(new HelpRun(step.Labels[index](), true));
                    i = close + 1;
                    continue;
                }

                AppLog.Warn("Help", $"step placeholder doesn't match its labels: {format}");
            }

            text.Append(c);
            i++;
        }

        if (text.Length > 0) runs.Add(new HelpRun(text.ToString(), false));
        return runs;
    }

    public static string Plain(HelpStep step) => string.Concat(Runs(step).Select(r => r.Text));
}
