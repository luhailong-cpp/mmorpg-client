using System;
using System.Collections.Generic;
using System.Linq;

namespace MmorpgClient.Game.Mail
{
    /// <summary>Mail rules and authoritative request boundary; no inventory, transport or persistence side effects.</summary>
    public sealed class MailUiState
    {
        private readonly Func<long> _clock;
        private List<MailMessage> _messages = new();
        private int _generation, _pending, _expiredCount;
        private long _started;
        private MailOperation _pendingOperation;
        public const string UnavailableMessage = "邮件暂未开放，敬请期待。";
        public bool IsDemo { get; private set; }
        public bool ServiceAvailable { get; private set; }
        public bool IsBusy => _pending != 0;
        public bool RequiresRefresh { get; private set; }
        public bool CanAct => (IsDemo || ServiceAvailable) && !IsBusy && !RequiresRefresh;
        public bool CanRefresh => !IsDemo && ServiceAvailable && !IsBusy;
        public ulong PlayerId { get; private set; }
        public string SelectedId { get; private set; }
        public MailCategory Category { get; private set; }
        public string Status { get; private set; } = UnavailableMessage;
        public long Now => _clock();
        public IReadOnlyList<MailMessage> Messages => _messages.Select(m => m.Copy()).ToArray();
        public IReadOnlyList<MailMessage> VisibleMessages => _messages.Where(Visible).Select(m => m.Copy()).ToArray();
        public MailMessage Selected => _messages.Find(m => m.Id == SelectedId)?.Copy();
        public int UnreadCount => _messages.Count(m => !m.IsRead);
        public int ClaimableCount => _messages.Count(HasValidAttachments);
        public int CleanableCount => _messages.Count(m => m.IsRead && !HasValidAttachments(m));
        public event Action Changed;
        public event Action<MailRequest> ActionRequested;
        public MailUiState(Func<long> clock = null) { _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        private bool Visible(MailMessage m) => Category == MailCategory.All || m.Category == Category;
        public bool IsExpired(MailMessage m) => m != null && m.ExpiresAt > 0 && m.ExpiresAt <= Now;
        public bool HasValidAttachments(MailMessage m) => m != null && !m.Claimed && !IsExpired(m) && m.Rewards.Any(r => r.Count > 0);
        public bool CanClaimSelected => CanAct && HasValidAttachments(Selected);
        public bool CanDeleteSelected => CanAct && Selected != null && !HasValidAttachments(Selected);

        public void Reset(ulong playerId = 0)
        {
            Invalidate(); _messages.Clear(); SelectedId = null; Category = MailCategory.All;
            PlayerId = playerId; RequiresRefresh = false; IsDemo = ServiceAvailable = false; Status = UnavailableMessage; _expiredCount = 0; Changed?.Invoke();
        }
        public void SetUnavailable(string message = null)
        { Invalidate(); ServiceAvailable = IsDemo = false; Status = message ?? UnavailableMessage; Changed?.Invoke(); }
        public void LoadDemo()
        {
            Invalidate(); RequiresRefresh = false; _messages = MailDemoData.Create(Now); IsDemo = true; ServiceAvailable = false;
            Category = MailCategory.All; SelectedId = "midautumn"; Status = "查看邮件不会自动领取，记得在到期前领取附件。";
            _expiredCount = _messages.Count(IsExpired); Changed?.Invoke();
        }
        public void SetSnapshot(IEnumerable<MailMessage> messages)
        {
            var next = Copy(messages); Invalidate(); RequiresRefresh = false; _messages = next; IsDemo = false; ServiceAvailable = true;
            EnsureSelection(); Status = ""; _expiredCount = _messages.Count(IsExpired); Changed?.Invoke();
        }
        private static List<MailMessage> Copy(IEnumerable<MailMessage> source)
        {
            var result = new List<MailMessage>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in source ?? Array.Empty<MailMessage>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) continue;
                var copy = item.Copy(); copy.Rewards.RemoveAll(r => r.Count <= 0); result.Add(copy);
            }
            return result;
        }
        private void EnsureSelection()
        { if (!_messages.Any(m => m.Id == SelectedId && Visible(m))) SelectedId = _messages.FirstOrDefault(Visible)?.Id; }
        public void SetCategory(MailCategory category)
        { Category = category; EnsureSelection(); Changed?.Invoke(); MarkSelectedRead(); }
        public void Select(string id)
        {
            if (!_messages.Any(m => m.Id == id && Visible(m))) return;
            SelectedId = id; Changed?.Invoke(); MarkSelectedRead();
        }
        public int MarkSelectedRead()
        {
            var m = Selected; return m == null || m.IsRead ? 0 : Request(MailOperation.Read, new[] { m.Id });
        }
        public int ClaimSelected()
        {
            var m = Selected;
            if (!HasValidAttachments(m)) { Status = IsExpired(m) ? "邮件已过期，附件无法领取。" : "没有可领取的附件。"; Changed?.Invoke(); return 0; }
            return Request(MailOperation.Claim, new[] { m.Id });
        }
        public int ClaimAll() => Request(MailOperation.ClaimAll, _messages.Where(HasValidAttachments).Select(m => m.Id));
        public int DeleteSelected()
        {
            var m = Selected;
            if (m == null) return 0;
            if (HasValidAttachments(m)) { Status = "这封邮件还有可领取的附件，请先领取再删除。"; Changed?.Invoke(); return 0; }
            return Request(MailOperation.Delete, new[] { m.Id });
        }
        public int ClearRead() => Request(MailOperation.ClearRead, _messages.Where(m => m.IsRead && !HasValidAttachments(m)).Select(m => m.Id));
        public int Refresh() => Request(MailOperation.Refresh, Array.Empty<string>());

        private int Request(MailOperation operation, IEnumerable<string> source)
        {
            if (!CanAct && !(operation == MailOperation.Refresh && CanRefresh)) return 0;
            var ids = source.Distinct().ToArray();
            if (ids.Length == 0 && operation != MailOperation.Refresh) return 0;
            if (IsDemo) { ApplyDemo(operation, ids); return 1; }
            if (ActionRequested == null) { SetUnavailable(); return 0; }
            _pending = NextGeneration(); _pendingOperation = operation; _started = Now; Status = "正在同步邮件，请稍候…";
            var request = new MailRequest(_pending, operation, ids); Changed?.Invoke();
            ActionRequested(request); return request.Generation;
        }
        private void ApplyDemo(MailOperation operation, string[] ids)
        {
            var affected = _messages.Where(m => ids.Contains(m.Id)).ToList();
            switch (operation)
            {
                case MailOperation.Read:
                    foreach (var m in affected) m.IsRead = true;
                    break;
                case MailOperation.Claim:
                case MailOperation.ClaimAll:
                    affected.RemoveAll(m => !HasValidAttachments(m));
                    foreach (var m in affected) m.Claimed = true;
                    Status = $"已领取 {affected.Count} 封邮件，共 {affected.Sum(m => m.Rewards.Count)} 项附件。";
                    break;
                case MailOperation.Delete:
                case MailOperation.ClearRead:
                    affected.RemoveAll(HasValidAttachments);
                    foreach (var m in affected) _messages.Remove(m);
                    Status = operation == MailOperation.ClearRead
                        ? $"已清理 {affected.Count} 封已读邮件，待领附件已保留。" : "邮件已删除。";
                    EnsureSelection();
                    break;
                case MailOperation.Refresh: Status = "当前为独立离线样例。"; break;
            }
            Changed?.Invoke();
        }
        public bool Complete(int generation, IEnumerable<MailMessage> authoritativeMessages, string status = "")
        {
            if (generation == 0 || generation != _pending || authoritativeMessages == null) return false;
            var next = Copy(authoritativeMessages); Invalidate(); _messages = next; EnsureSelection();
            ServiceAvailable = true; IsDemo = false; RequiresRefresh = false; Status = status ?? ""; _expiredCount = _messages.Count(IsExpired); Changed?.Invoke(); return true;
        }
        public bool Fail(int generation, string message)
        {
            if (generation == 0 || generation != _pending) return false;
            Invalidate(); Status = message ?? "请求失败，请重试。"; Changed?.Invoke(); return true;
        }
        public void Tick()
        {
            if (IsBusy && Now - _started >= 10) { RequiresRefresh = _pendingOperation != MailOperation.Read && _pendingOperation != MailOperation.Refresh; Fail(_pending, RequiresRefresh ? "操作结果尚未确认，请刷新邮件后再试。" : "邮件请求超时，请稍后刷新。"); return; }
            int count = _messages.Count(IsExpired);
            if (count != _expiredCount) { _expiredCount = count; Changed?.Invoke(); }
        }
        private int NextGeneration() { unchecked { ++_generation; } if (_generation == 0) ++_generation; return _generation; }
        private void Invalidate() { NextGeneration(); _pending = 0; }
    }
}
