using DeepDenoiseClient.Models;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace DeepDenoiseClient.Services.Common
{
    public interface IStatusbarService
    {
        SystemStatusModel Current { get; }
        IDisposable Begin();                 // 시간 측정 시작
        void Success(string message);        // 성공
        void Fail(string message);           // 실패
    }

    public sealed class StatusbarService : IStatusbarService
    {
        private readonly Dispatcher _ui;
        private readonly SystemStatusModel _state = new();
        private Stopwatch? _sw;
        private int _opDepth;

        public SystemStatusModel Current => _state;

        public StatusbarService()
        {
            _ui = Dispatcher.CurrentDispatcher;
        }

        public IDisposable Begin()
        {
            Interlocked.Increment(ref _opDepth);
            if (_opDepth == 1)
                _sw = Stopwatch.StartNew();
            return new Op(this);
        }

        public void Success(string message) => Finish("Success", message);
        public void Fail(string message) => Finish("Fail", message);

        private void Finish(string status, string message)
        {
            var elapsed = _sw?.Elapsed ?? TimeSpan.Zero;
            PostUI(() =>
            {
                _state.Status = status;   // Toolkit가 자동으로 PropertyChanged 발생
                _state.Message = message;
                _state.Elapsed = elapsed;
            });
            _sw?.Stop();
            _sw = null;
            Interlocked.Exchange(ref _opDepth, 0);
        }

        private void PostUI(Action a)
        {
            if (_ui.CheckAccess()) a();
            else _ui.BeginInvoke(a);
        }

        private sealed class Op : IDisposable
        {
            private readonly StatusbarService _svc;
            private bool _disposed;
            public Op(StatusbarService svc) => _svc = svc;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                // Success/Fail 미호출 시 상태 유지
            }
        }
    }
}
