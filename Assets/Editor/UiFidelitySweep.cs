#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Offline production-view capture inventory. Capturing is not a visual-fidelity verdict.</summary>
public static class UiFidelitySweep
{
    private const string Root="E:/work/image/docs/ui-fidelity-20261016/native";
    [Serializable] private sealed class Item { public string module,status,directory,error;public int screenshots; }
    [Serializable] private sealed class Report
    { public string status="captured",completedAtUtc,unityVersion;public bool liveServerVerified=false,visualFidelityVerified=false;public Item[] modules; }
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
        Directory.CreateDirectory(Root);
        try{CaptureUpdated();}catch(Exception error){File.WriteAllText(Path.Combine(Root,"updated-capture-error.txt"),error.ToString());Debug.LogException(error);}
        Capture();
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
