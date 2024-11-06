using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RuntimeIcons.Utils;

public static class ThrottleUtils
{
    internal static readonly HashSet<Semaphore> UpdateSemaphores = new();
    internal static readonly HashSet<Semaphore> FixedUpdateSemaphores = new();

    internal static GameObject InitComponents(string name = $"{nameof(RuntimeIcons)}.SemaphoreHandler")
    {
        var go = new GameObject(name, typeof(ThrottleComponent));
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        return go;
    }
    
    internal class ThrottleComponent : MonoBehaviour
    {
        private void FixedUpdate()
        {
            foreach (var semaphore in FixedUpdateSemaphores)
            {
                semaphore.Update();
            }
        }

        private void LateUpdate()
        {
            foreach (var semaphore in UpdateSemaphores)
            {
                semaphore.Update();
            }
        }
    }
    
    public class Semaphore : IDisposable
    {
        public static Semaphore CreateNewSemaphore(int initCount = 1, int interval = 1, SemaphoreTimeUnit timeUnit = SemaphoreTimeUnit.Manual)
        {
            var semaphore = new Semaphore(initCount, interval);

            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (timeUnit)
            {
                case SemaphoreTimeUnit.Update:
                    UpdateSemaphores.Add(semaphore);
                    break;
                case SemaphoreTimeUnit.FixedUpdate:
                    FixedUpdateSemaphores.Add(semaphore);
                    break;
            }

            return semaphore;
        }
        
        private bool _disposed = false;

        private int _lastUpdate = 0;
        
        private readonly int _interval;

        private readonly int _initCount;

        private int _count;

        private Semaphore(int initCount, int interval = 1)
        {
            this._interval = interval;

            _count = this._initCount = initCount;
        }

        public bool TryAcquire()
        {
            if (_disposed)
                throw new NotSupportedException("Object was already disposed");
            
            var value = Interlocked.Decrement(ref _count);
            return value >= 0;
        }

        public void Reset()
        {
            if (_disposed)
                throw new NotSupportedException("Object was already disposed");
            
            Interlocked.Exchange(ref _count, _initCount);
        }

        public void Update(int frames = 1)
        {
            lock (this)
            {
                _lastUpdate += frames;

                _lastUpdate %= _interval;
                
                if (_lastUpdate == 0)
                    Reset();
            }
        } 

        public void Dispose()
        {
            if (_disposed)
                throw new NotSupportedException("Object was already disposed");

            this._disposed = true;

            UpdateSemaphores.Remove(this);
            FixedUpdateSemaphores.Remove(this);
        }
    }
    
    public enum SemaphoreTimeUnit
    {
        Manual,
        Update,
        FixedUpdate
    }
    
}