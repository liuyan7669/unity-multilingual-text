using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cowart.MultilingualText
{
    public interface IMultilingualTextObserver
    {
        void OnLanguageChanged(SupportedLanguage language);
    }

    internal sealed class MultilingualTextObserverRegistry
    {
        private readonly List<IMultilingualTextObserver> observers = new List<IMultilingualTextObserver>();

        public void Register(IMultilingualTextObserver observer)
        {
            if (observer != null && !observers.Contains(observer))
            {
                observers.Add(observer);
            }
        }

        public void Unregister(IMultilingualTextObserver observer)
        {
            observers.Remove(observer);
        }

        public void Notify(SupportedLanguage language)
        {
            IMultilingualTextObserver[] snapshot = observers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                IMultilingualTextObserver observer = snapshot[i];
                if (observer == null)
                {
                    continue;
                }

                try
                {
                    observer.OnLanguageChanged(language);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
