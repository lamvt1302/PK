using System;

namespace PK
{
    /// <summary>
    /// Giữ request id cho "action đang chạy" để retry reuse đúng ID (idempotency).
    /// </summary>
    public class RequestIdStore
    {
        private Guid? _current;

        public Guid NewAction()
        {
            _current = Guid.NewGuid();
            return _current.Value;
        }

        public Guid? Current => _current;

        public void Clear()
        {
            _current = null;
        }
    }
}

