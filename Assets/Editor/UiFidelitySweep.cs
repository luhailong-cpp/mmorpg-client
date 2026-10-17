#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Offline production-view capture inventory. Capturing is not a visual-fidelity verdict.</summary>
public static class UiFidelitySweep
{
    private const string Root="E:/work/image/docs/ui-fidelity-20261016/native";
    [Serializable] private sealed class Item { public string module,status,directory,error;public int screenshots; }
    [Serializable] private sealed class Report
    { public string status="captured",completedAtUtc,unityVersion;public bool liveServerVerified=false,visualFidelityVerified=false;public Item[] modules; }
    private static double BattleDeadline;
    public static void CaptureBattle()
    {
        PrepareBatchScene();Directory.CreateDirectory(Root+"/battle");
        ClientUiRefreshVerification.OutputDirectory=Root+"/battle";
        BattleDeadline=EditorApplication.timeSinceStartup+120;
        ClientBattleUiQaCapture.StartCaptureAll();
        EditorApplication.update+=FinishBattle;
    }
    private static void FinishBattle()
    {
        if(ClientBattleUiQaCapture.IsRunning&&EditorApplication.timeSinceStartup<BattleDeadline)return;
        EditorApplication.update-=FinishBattle;
        string status=ClientBattleUiQaCapture.IsRunning?"timed-out":ClientBattleUiQaCapture.Status;
        File.WriteAllText(Root+"/battle/capture-status.txt",status+"\nOffline production controls; live server not verified.\n");
        if(Application.isBatchMode)EditorApplication.Exit(status.StartsWith("complete")?0:1);
    }
    public static void CaptureCatalog()
    {
        PrepareBatchScene();JubaozhaiUiVerification.OutputDirectory="E:/work/image/designs/jubaozhai-ui/fidelity-20261016/qa/native";
        JubaozhaiUiVerification.CaptureAll();
    }
    public static void CaptureCatalogAndBattle()
    {
        CaptureCatalog();CaptureBattle();
    }
    public static void CapturePanelsAndChannels()
    {
        PrepareBatchScene();
        AttributePanelV10Verification.OutputDirectory="E:/work/image/designs/attribute-panels/fidelity-20261016/qa/native";
        AttributePanelV10Verification.CaptureAll();
        SkillUiVerification.CaptureAll();
        SceneChannelFidelityVerification.OutputDirectory="E:/work/image/designs/scene-select-ui-v1/fidelity-20261016/qa/native";
        SceneChannelFidelityVerification.Capture();
        Debug.Log("UI_FIDELITY_PANELS_CHANNELS_CAPTURE_OK");
    }
    public static void CaptureSkillsAndChannels()
    {
        PrepareBatchScene();SkillUiVerification.CaptureAll();
        SceneChannelFidelityVerification.OutputDirectory="E:/work/image/designs/scene-select-ui-v1/fidelity-20261016/qa/native";
        SceneChannelFidelityVerification.Capture();
        Debug.Log("UI_FIDELITY_SKILLS_CHANNELS_CAPTURE_OK");
    }
    public static void CaptureUpdated()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stable Edit Mode required.");
        SocialFidelityVerification.OutputDirectory="E:/work/image/designs/social-ui-v1/fidelity-20261016/qa/final";
        SocialFidelityVerification.Capture();
        TeamFidelityVerification.OutputDirectory="E:/work/image/designs/team-ui-v2/fidelity-20261016/qa/native";
        TeamFidelityVerification.Capture();
        MailUiVerification.OutputDirectory="E:/work/image/designs/mail-ui-v1/fidelity-20261016/qa/native";
        MailUiVerification.CaptureAll();
        GameplayUiVerification.OutputDirectory="E:/work/image/designs/gameplay-ui/fidelity-20261016/qa/native";
        GameplayUiVerification.CaptureAll();
        SceneChannelFidelityVerification.Capture();
        WorldMapFidelityVerification.Capture();
        Debug.Log("UPDATED_UI_CAPTURE_OK|social-team-mail-gameplay-channels-world-map");
    }
    public static void CaptureEverything()
    {
        PrepareBatchScene();
        Directory.CreateDirectory(Root);
        try{CaptureUpdated();}catch(Exception error){File.WriteAllText(Path.Combine(Root,"updated-capture-error.txt"),error.ToString());Debug.LogException(error);}
        Capture();
    }
    private static void PrepareBatchScene()
    {
        // Only this fresh batch editor has no user scene. Interactive editors
        // keep their existing scene and are never switched by this helper.
        if(Application.isBatchMode&&string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity",OpenSceneMode.Single);
    }
    public static void CaptureOutstanding()
    {
        PrepareBatchScene();Directory.CreateDirectory(Root);var items=new List<Item>();
        void Run(string name,string directory,Action capture)
        {
            Directory.CreateDirectory(directory);var item=new Item{module=name,directory=directory,status="captured"};
            try{capture();}catch(Exception error){item.status="capture-failed";item.error=error.ToString();}
            item.screenshots=Directory.GetFiles(directory,"*.png",SearchOption.AllDirectories).Length;items.Add(item);
        }
        Run("gameplay","E:/work/image/designs/gameplay-ui/fidelity-20261016/qa/native",()=>{
            GameplayUiVerification.OutputDirectory="E:/work/image/designs/gameplay-ui/fidelity-20261016/qa/native";GameplayUiVerification.CaptureAll();});
        Run("channels","E:/work/image/designs/scene-select-ui-v1/fidelity-20261016/qa/native",()=>{
            SceneChannelFidelityVerification.OutputDirectory="E:/work/image/designs/scene-select-ui-v1/fidelity-20261016/qa/native";SceneChannelFidelityVerification.Capture();});
        Run("world-map","E:/work/image/designs/world-map-original-20260925/fidelity-20261016/qa/native",()=>{
            WorldMapFidelityVerification.OutputDirectory="E:/work/image/designs/world-map-original-20260925/fidelity-20261016/qa/native";WorldMapFidelityVerification.Capture();});
        Run("login-server-role",Root+"/core",()=>{ClientUiRefreshVerification.OutputDirectory=Root+"/core";ClientUiRefreshVerification.CaptureAll();});
        Run("character-pet-attributes",Root+"/attributes",()=>{AttributePanelV10Verification.OutputDirectory=Root+"/attributes";AttributePanelV10Verification.CaptureAll();});
        Run("equipment-details-wish",Root+"/equipment",()=>{EquipmentUiVerification.OutputDirectory=Root+"/equipment";EquipmentUiVerification.CaptureAll();});
        File.WriteAllText(Path.Combine(Root,"outstanding-inventory.json"),JsonUtility.ToJson(new Report{
            status=items.TrueForAll(i=>i.status=="captured")?"captured":"captured-with-gaps",completedAtUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,modules=items.ToArray()},true));
        Debug.Log("UI_FIDELITY_OUTSTANDING_CAPTURE_FINISHED");
    }
    public static void Capture()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stable Edit Mode required.");
        Directory.CreateDirectory(Root);var items=new List<Item>();
        void Run(string name,string directory,Action capture)
        {
            Directory.CreateDirectory(directory);var item=new Item{module=name,directory=directory,status="captured"};
            try{capture();}catch(Exception error){item.status="capture-failed";item.error=error.ToString();}
            item.screenshots=Directory.GetFiles(directory,"*.png",SearchOption.AllDirectories).Length;items.Add(item);
            File.WriteAllText(Path.Combine(Root,"capture-inventory.json"),JsonUtility.ToJson(new Report{
                status="running",completedAtUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,modules=items.ToArray()},true));
        }
        Run("login-server-role",Root+"/core",()=>{ClientUiRefreshVerification.OutputDirectory=Root+"/core";ClientUiRefreshVerification.CaptureAll();});
        Run("character-pet-attributes",Root+"/attributes",()=>{AttributePanelV10Verification.OutputDirectory=Root+"/attributes";AttributePanelV10Verification.CaptureAll();});
        Run("equipment-details-wish",Root+"/equipment",()=>{EquipmentUiVerification.OutputDirectory=Root+"/equipment";EquipmentUiVerification.CaptureAll();});
        Run("world-travel",Root+"/world-travel",()=>{CityTravelUiVerification.OutputDirectory=Root+"/world-travel";CityTravelUiVerification.CaptureAll();});
        Run("scene-channels",Root+"/channels",()=>{SceneChannelUiVerification.OutputDirectory=Root+"/channels";SceneChannelUiVerification.CaptureAll();});
        Run("mystery-merchant",Root+"/merchant",()=>{MysteryMerchantUiVerification.OutputDirectory=Root+"/merchant";MysteryMerchantUiVerification.CaptureAll();});
        Run("guild",Root+"/guild",()=>{
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("GuildUiVerification")).FirstOrDefault(t=>t!=null)
                ??throw new InvalidOperationException("Guild editor verification assembly is unavailable.");
            type.GetField("OutputDirectory",BindingFlags.Public|BindingFlags.Static).SetValue(null,Root+"/guild");
            type.GetMethod("CaptureAll",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
        });
        Run("jubaozhai",Root+"/jubaozhai",()=>{JubaozhaiUiVerification.OutputDirectory=Root+"/jubaozhai";JubaozhaiUiVerification.CaptureAll();});
        Run("skills",SkillUiVerification.OutputDirectory,SkillUiVerification.CaptureAll);
        File.WriteAllText(Path.Combine(Root,"capture-inventory.json"),JsonUtility.ToJson(new Report{
            status=items.TrueForAll(i=>i.status=="captured")?"captured":"captured-with-gaps",completedAtUtc=DateTime.UtcNow.ToString("O"),
            unityVersion=Application.unityVersion,modules=items.ToArray()},true));
        Debug.Log("UI_FIDELITY_SWEEP_FINISHED|modules="+items.Count+"|visualFidelityVerified=false");
    }
}
#endif
