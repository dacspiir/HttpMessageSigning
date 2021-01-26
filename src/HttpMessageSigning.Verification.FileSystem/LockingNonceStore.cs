﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dalion.HttpMessageSigning.Verification.FileSystem {
    internal class LockingNonceStore : INonceStore {
        private static readonly TimeSpan MaxLockWaitTime = TimeSpan.FromSeconds(1);

        private readonly INonceStore _decorated;
        private readonly SemaphoreSlim _semaphore;

        public LockingNonceStore(INonceStore decorated, ISemaphoreFactory semaphoreFactory) {
            if (semaphoreFactory == null) throw new ArgumentNullException(nameof(semaphoreFactory));
            _decorated = decorated ?? throw new ArgumentNullException(nameof(decorated));
            _semaphore = semaphoreFactory.CreateLock();
        }

        public void Dispose() {
            _semaphore?.Dispose();
            _decorated?.Dispose();
        }

        public async Task Register(Nonce nonce) {
            await _semaphore.WaitAsync(MaxLockWaitTime, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            try {
                await _decorated.Register(nonce);
            }
            finally {
                _semaphore.Release();
            }
        }

        public async Task<Nonce> Get(KeyId clientId, string nonceValue) {
            await _semaphore.WaitAsync(MaxLockWaitTime, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            try {
                return await _decorated.Get(clientId, nonceValue);
            }
            finally {
                _semaphore.Release();
            }
        }
    }
}