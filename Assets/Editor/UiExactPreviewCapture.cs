#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UiExactPreviewCapture
{
    public const string Root="E:/work/image/docs/ui-fidelity-exact-20261021/native";
    [Serializable] public sealed class Item { public string module,status,error,directory; public int screenshots; }
    [Serializable] public sealed class Report { public string capturedAtUtc;public bool fullVisualAcceptance;public Item[] modules; }
    static readonly List<Item> Items=new List<Item>();
    static string Batch="capture";
    static void Prepare()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required");
        if(Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity",OpenSceneMode.Single);
        Directory.CreateDirectory(Root);Items.Clear();
    }
    static void Run(string module,Action<string> capture)
    {
        var item=new Item {module=module,status="captured",directory=Root+"/"+module,error=""};
        Directory.CreateDirectory(item.directory);
        try {capture(item.directory);}catch(Exception e){item.status="failed";item.error=e.ToString();Debug.LogException(e);}
        item.screenshots=Directory.GetFiles(item.directory,"*.png",SearchOption.AllDirectories).Length;Items.Add(item);
        File.WriteAllText(Root+"/"+Batch+"-inventory.json",JsonUtility.ToJson(new Report{capturedAtUtc=DateTime.UtcNow.ToString("O"),fullVisualAcceptance=false,modules=Items.ToArray()},true));
    }
    public static void PhaseOne()
    {
        Batch="phase-one";Prepare();
        Run("gameplay",p=>{GameplayUiVerification.OutputDirectory=p;GameplayUiVerification.CaptureAll();});
        Run("social",p=>{SocialFidelityVerification.OutputDirectory=p;SocialFidelityVerification.Capture();});
        Run("mail",p=>{MailUiVerification.OutputDirectory=p;MailUiVerification.CaptureAll();});
        Run("attributes",p=>{AttributePanelV10Verification.OutputDirectory=p;AttributePanelV10Verification.CaptureAll();});
        Run("skills",p=>{SkillUiVerification.CaptureAll();foreach(var f in Directory.GetFiles(SkillUiVerification.OutputDirectory))File.Copy(f,Path.Combine(p,Path.GetFileName(f)),true);});
        Run("channels",p=>{SceneChannelFidelityVerification.OutputDirectory=p;SceneChannelFidelityVerification.Capture();});
        Run("world",p=>{WorldMapFidelityVerification.OutputDirectory=p;WorldMapFidelityVerification.Capture();});
        Debug.Log("UI_EXACT_PHASE_ONE_CAPTURE_FINISHED");
    }
    public static void PhaseTwo()
    {
        Batch="phase-two";Prepare();MmorpgClient.UI.EditorTools.QdaoUguiBuilder.BuildAll();
        Run("core",p=>{ClientUiRefreshVerification.OutputDirectory=p;ClientUiRefreshVerification.CaptureAll();});
        Run("team",p=>{TeamFidelityVerification.OutputDirectory=p;TeamFidelityVerification.Capture();});
        Run("guild",p=>{
            var t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("GuildUiVerification")).First(t=>t!=null);
            t.GetField("OutputDirectory",BindingFlags.Public|BindingFlags.Static).SetValue(null,p);
            t.GetMethod("CaptureAll",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
        });
        Run("jubaozhai",p=>{JubaozhaiUiVerification.OutputDirectory=p;JubaozhaiUiVerification.CaptureAll();});
        Run("equipment",p=>{EquipmentUiVerification.OutputDirectory=p;EquipmentUiVerification.CaptureAll();});
        Debug.Log("UI_EXACT_PHASE_TWO_CAPTURE_FINISHED");
    }
    public static void AttributesOnly()
    {
        Batch="attributes";Prepare();
        Run("attributes",p=>{AttributePanelV10Verification.OutputDirectory=p;AttributePanelV10Verification.CaptureAll();});
        Debug.Log("UI_EXACT_ATTRIBUTES_CAPTURE_FINISHED");
    }
    public static void FinalRemaining()
    {
        PhaseTwo();
        Run("hud",p=>{WorldHudVerification.OutputDirectory=p;WorldHudVerification.CaptureAll();});
        Run("transition",p=>{TransitionNativeCapture20261021.OutputDirectory=p;TransitionNativeCapture20261021.CaptureAll();});
        Debug.Log("UI_EXACT_FINAL_REMAINING_CAPTURE_FINISHED");
    }
    public static void FinalCorrections()
    {
        Batch="final-corrections";Prepare();MmorpgClient.UI.EditorTools.QdaoUguiBuilder.BuildAll();
        Run("core",p=>{ClientUiRefreshVerification.OutputDirectory=p;ClientUiRefreshVerification.CaptureAll();});
        Run("transition",p=>{TransitionNativeCapture20261021.OutputDirectory=p;TransitionNativeCapture20261021.CaptureAll();});
        Debug.Log("UI_EXACT_FINAL_CORRECTIONS_CAPTURE_FINISHED");
    }
    static double BattleDeadline;
    static int BattleResolution;
    public static void BattleOnly()
    {
        Prepare();Directory.CreateDirectory(Root+"/battle");
        ClientUiRefreshVerification.OutputDirectory=Root+"/battle";
        BattleResolution=0;StartBattleResolution();
        EditorApplication.update+=FinishBattle;
    }
    static void StartBattleResolution()
    {
        ClientBattleUiQaCapture.OutputWidth=BattleResolution==0?2560:1920;
        ClientBattleUiQaCapture.OutputHeight=1080;
        BattleDeadline=EditorApplication.timeSinceStartup+120;
        ClientBattleUiQaCapture.StartCaptureAll();
    }
    static void FinishBattle()
    {
        if(ClientBattleUiQaCapture.IsRunning&&EditorApplication.timeSinceStartup<BattleDeadline)return;
        string status=ClientBattleUiQaCapture.IsRunning?"timed-out":ClientBattleUiQaCapture.Status;
        File.WriteAllText(Root+"/battle/capture-status-"+(BattleResolution==0?"2560":"1920")+"x1080.txt",status+"\nProduction BattleUi controls, disconnected fixture.\n");
        if(status.StartsWith("complete")&&BattleResolution==0)
        {BattleResolution=1;StartBattleResolution();return;}
        EditorApplication.update-=FinishBattle;
        if(Application.isBatchMode)EditorApplication.Exit(status.StartsWith("complete")?0:1);
    }
}
#endif
