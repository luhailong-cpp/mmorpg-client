#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Explicit, single-use verification job. No flag means no import, capture, test, locking or scene work.
/// Each stage runs on the editor main thread; locks are released on completion, error, shutdown or watchdog.
/// </summary>
[InitializeOnLoad]
public static class MailSocialVerificationJob
{
    public const string FlagPath="E:/work/image/designs/social-ui-v1/unity-slices/qa/run-native-verification.flag";
    private const string SocialDirectory="E:/work/image/designs/social-ui-v1/unity-slices/qa";
    private const string MailDirectory="E:/work/image/designs/mail-ui-v1/unity-slices/qa";
    private const double StableSeconds=5,WatchdogSeconds=300;
    private enum Phase { Idle, Import, SocialCapture, MailCapture, StartTests, WaitTests, SocialAssets, MailAssets, Finish }
    private static Phase _phase;
    private static bool _running,_refreshLocked,_reloadLocked,_importPassed;
    private static double _stableSince=-1,_startedAt,_nextIdleCheck;
    private static DateTime _testsStartedUtc;
    private static string _previousTestReport,_oldSocialOutput,_oldMailOutput,_oldTestsOutput;
    private static JobReport _report;
    private static readonly List<StageResult> Stages=new();
    private static readonly List<StageError> Errors=new();

    [Serializable] private sealed class JobReport
    {
        public string status,runId,startedAtUtc,completedAtUtc,phase,consumedFlag,unityVersion;
        public bool success,liveServerVerified=false;
        public double seconds;
        public string testReportPath=SocialDirectory+"/editmode-results.json";
        public string mailCaptureDirectory=MailDirectory,socialCaptureDirectory=SocialDirectory;
        public StageResult[] stages;
        public StageError[] errors;
        public TestSummary tests;
    }
    [Serializable] private sealed class StageResult
    { public string stage,status,message,completedAtUtc; }
    [Serializable] private sealed class StageError
    { public string stage,message,stacktrace,occurredAtUtc; }
    [Serializable] private sealed class TestSummary
    {
        public string status,unityVersion,completedAtUtc;
        public int passed,failed,skipped,inconclusive,assertions;
        public double seconds;
        public string[] assemblies,failures;
    }

    static MailSocialVerificationJob()
    {
        EditorApplication.update+=Update;
        EditorApplication.quitting+=Interrupted;
        AssemblyReloadEvents.beforeAssemblyReload+=Interrupted;
    }

    private static void Update()
    {
        if(!_running)
        {
            if(EditorApplication.timeSinceStartup<_nextIdleCheck)return;
            _nextIdleCheck=EditorApplication.timeSinceStartup+.25;
            if(!File.Exists(FlagPath)){_stableSince=-1;return;}
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||MailSocialTestRunner.IsRunning)
            { _stableSince=-1;return; }
            if(_stableSince<0){_stableSince=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-_stableSince<StableSeconds)return;
            Begin();
            return;
        }
        try
        {
            if(EditorApplication.timeSinceStartup-_startedAt>=WatchdogSeconds)
            {
                // The existing runner does not expose its run GUID; do not cancel unrelated Unity test jobs.
                AddError("watchdog",new TimeoutException("Verification exceeded five minutes. Locks are being released. Mail/social TestRunner still running: "+MailSocialTestRunner.IsRunning));
                Finish();return;
            }
            if(EditorApplication.isPlayingOrWillChangePlaymode)
            { AddError("editor-state",new InvalidOperationException("Edit-mode verification interrupted by Play mode."));Finish();return; }
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            switch(_phase)
            {
                case Phase.Import:
                    _importPassed=RunStage("import",MailSocialAssetImport.ImportApproved);
                    _phase=Phase.SocialCapture;break;
                case Phase.SocialCapture:
                    if(_importPassed)RunStage("social-capture",SocialUiVerification.CaptureAll);
                    else Skip("social-capture","Approved resource import failed; screenshots were not attempted.");
                    _phase=Phase.MailCapture;break;
                case Phase.MailCapture:
                    if(_importPassed)RunStage("mail-capture",MailUiVerification.CaptureAll);
                    else Skip("mail-capture","Approved resource import failed; screenshots were not attempted.");
                    _phase=Phase.StartTests;break;
                case Phase.StartTests:
                    if(RunStage("test-dispatch",StartTests))_phase=Phase.WaitTests;
                    else _phase=Phase.SocialAssets;
                    break;
                case Phase.WaitTests:
                    if(MailSocialTestRunner.IsRunning)return;
                    RunStage("test-results",ReadTestResults);
                    _phase=Phase.SocialAssets;break;
                case Phase.SocialAssets:
                    RunStage("social-preview-assets",()=>BuildMissing("Assets/Scenes/SocialPreview.unity","Assets/Prefabs/UI/SocialOfflinePreview.prefab",SocialUiVerification.BuildPreviewAssets));
                    _phase=Phase.MailAssets;break;
                case Phase.MailAssets:
                    RunStage("mail-preview-assets",()=>BuildMissing("Assets/Scenes/MailOfflinePreview.unity","Assets/Prefabs/UI/MailOfflinePreview.prefab",MailUiVerification.BuildPreviewAssets));
                    _phase=Phase.Finish;break;
                case Phase.Finish: Finish();return;
            }
            WriteReport();
        }
        catch(Exception exception)
        { AddError("coordinator",exception);Finish(); }
    }

    private static void Begin()
    {
        _stableSince=-1;_startedAt=EditorApplication.timeSinceStartup;Stages.Clear();Errors.Clear();
        string runId=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
        string consumed=Path.Combine(SocialDirectory,"run-native-verification.consumed-"+runId+".flag");
        // Move is the single-use claim. A consumed trigger is never retried automatically.
        try { File.Move(FlagPath,consumed); }
        catch(Exception exception)
        {
            // If another caller already consumed it, no job is ours. Avoid a per-frame retry loop on I/O failure.
            _stableSince=-1;_nextIdleCheck=EditorApplication.timeSinceStartup+WatchdogSeconds;
            Debug.LogError("MAIL_SOCIAL_JOB_FLAG_NOT_CONSUMED: "+exception);
            return;
        }
        _report=new JobReport { status="running",runId=runId,startedAtUtc=DateTime.UtcNow.ToString("O"),
            consumedFlag=consumed,unityVersion=Application.unityVersion };
        _oldSocialOutput=SocialUiVerification.OutputDirectory;
        _oldMailOutput=MailUiVerification.OutputDirectory;
        _oldTestsOutput=MailSocialTestRunner.OutputDirectory;
        _running=true;_importPassed=false;_phase=Phase.Import;
        try
        {
            Directory.CreateDirectory(SocialDirectory);Directory.CreateDirectory(MailDirectory);
            AssetDatabase.DisallowAutoRefresh();_refreshLocked=true;
            EditorApplication.LockReloadAssemblies();_reloadLocked=true;
            SocialUiVerification.OutputDirectory=SocialDirectory;
            MailUiVerification.OutputDirectory=MailDirectory;
            MailSocialTestRunner.OutputDirectory=SocialDirectory;
            WriteReport();
            Debug.Log("MAIL_SOCIAL_JOB_STARTED|"+runId);
        }
        catch(Exception exception)
        { AddError("setup",exception);Finish(); }
    }

    private static bool RunStage(string stage,Action action)
    {
        try
        {
            action();Stages.Add(new StageResult{stage=stage,status="passed",completedAtUtc=DateTime.UtcNow.ToString("O")});
            return true;
        }
        catch(Exception exception)
        {
            AddError(stage,exception);
            Stages.Add(new StageResult{stage=stage,status="error",message=exception.Message,completedAtUtc=DateTime.UtcNow.ToString("O")});
            return false;
        }
    }
    private static void Skip(string stage,string message)
    { Stages.Add(new StageResult{stage=stage,status="skipped",message=message,completedAtUtc=DateTime.UtcNow.ToString("O")}); }
    private static void AddError(string stage,Exception exception)
    {
        Errors.Add(new StageError{stage=stage,message=exception.Message,stacktrace=exception.ToString(),occurredAtUtc=DateTime.UtcNow.ToString("O")});
        Debug.LogError("MAIL_SOCIAL_JOB_ERROR|"+stage+"|"+exception);
    }

    private static void StartTests()
    {
        string path=Path.Combine(SocialDirectory,"editmode-results.json");
        _previousTestReport=File.Exists(path)?File.ReadAllText(path):null;
        _testsStartedUtc=DateTime.UtcNow;
        MailSocialTestRunner.Run();
    }
    private static void ReadTestResults()
    {
        string path=Path.Combine(SocialDirectory,"editmode-results.json");
        if(!File.Exists(path))throw new InvalidDataException("Unity Test Runner finished without an actual JSON result.");
        string json=File.ReadAllText(path);
        if(json==_previousTestReport)throw new InvalidDataException("Unity Test Runner report is unchanged from the previous run.");
        var summary=JsonUtility.FromJson<TestSummary>(json);
        if(summary==null||!DateTime.TryParse(summary.completedAtUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var completed)||
           completed.ToUniversalTime()<_testsStartedUtc.AddSeconds(-1))
            throw new InvalidDataException("Unity Test Runner result has no fresh completion timestamp.");
        _report.tests=summary;
        string[] expected={"MmorpgClient.Tests.EditMode.Mail","MmorpgClient.Tests.EditMode.Social"};
        if(summary.assemblies==null||expected.Any(name=>!summary.assemblies.Contains(name)))
            throw new InvalidDataException("Unity Test Runner result does not cover both requested assemblies.");
        if(!File.Exists(Path.Combine(SocialDirectory,"editmode-results.xml")))
            throw new InvalidDataException("Unity Test Runner XML result is missing.");
        if(summary.status!="passed"||summary.passed<=0||summary.failed!=0||summary.skipped!=0||summary.inconclusive!=0)
            throw new InvalidOperationException("Actual Unity tests need attention: passed="+summary.passed+", failed="+summary.failed+
                ", skipped="+summary.skipped+", inconclusive="+summary.inconclusive+"\n"+string.Join("\n",summary.failures??Array.Empty<string>()));
    }
    private static void BuildMissing(string scenePath,string prefabPath,Action builder)
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        bool scene=File.Exists(Path.Combine(root,scenePath)),prefab=File.Exists(Path.Combine(root,prefabPath));
        if(scene&&prefab)return;
        if(scene||prefab)throw new InvalidOperationException("Only one preview asset exists. Preserve it and repair the missing asset explicitly: "+scenePath+" / "+prefabPath);
        builder();
        if(!File.Exists(Path.Combine(root,scenePath))||!File.Exists(Path.Combine(root,prefabPath)))
            throw new IOException("Preview builder returned without both requested assets: "+scenePath+" / "+prefabPath);
    }
    private static void Interrupted()
    {
        if(!_running)return;
        AddError("interrupted",new OperationCanceledException("Editor is quitting or reloading assemblies."));
        Finish();
    }
    private static void Finish()
    {
        if(!_running)return;
        _report.status=Errors.Count==0?"success":"error";_report.success=Errors.Count==0;
        _report.completedAtUtc=DateTime.UtcNow.ToString("O");_report.seconds=EditorApplication.timeSinceStartup-_startedAt;
        try { WriteReport(); }
        finally
        {
            _running=false;_phase=Phase.Idle;_stableSince=-1;
            SocialUiVerification.OutputDirectory=_oldSocialOutput;
            MailUiVerification.OutputDirectory=_oldMailOutput;
            MailSocialTestRunner.OutputDirectory=_oldTestsOutput;
            // Release refresh inhibition while reloads remain locked, then release the assembly lock.
            try
            {
                if(_refreshLocked){_refreshLocked=false;AssetDatabase.AllowAutoRefresh();}
            }
            catch(Exception exception){Debug.LogError("MAIL_SOCIAL_JOB_REFRESH_UNLOCK_ERROR: "+exception);}
            finally
            {
                if(_reloadLocked){_reloadLocked=false;EditorApplication.UnlockReloadAssemblies();}
            }
        }
        Debug.Log("MAIL_SOCIAL_JOB_DONE|"+_report.runId+"|"+_report.status+"|errors="+Errors.Count);
    }
    private static void WriteReport()
    {
        if(_report==null)return;
        _report.phase=_phase.ToString();_report.seconds=EditorApplication.timeSinceStartup-_startedAt;
        _report.stages=Stages.ToArray();_report.errors=Errors.ToArray();
        try { File.WriteAllText(Path.Combine(SocialDirectory,"verification-job.json"),JsonUtility.ToJson(_report,true)); }
        catch(Exception exception){Debug.LogError("MAIL_SOCIAL_JOB_REPORT_WRITE_ERROR: "+exception);}
    }
}
#endif
