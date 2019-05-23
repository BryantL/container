﻿using System;
using System.Security;

namespace Unity.Storage
{
    [SecuritySafeCritical]
    public class Pipelines
    {
        #region Fields

        private readonly int _prime;
        public readonly int[] Buckets;
        public readonly Entry[] Entries;
        public int Count;

        #endregion


        #region Constructors


        public Pipelines()
        {
            var size = Primes[0];

            Buckets = new int[size];
            Entries = new Entry[size];
            Count = 0;

#if !NET40
            unsafe
            {
                fixed (int* bucketsPtr = Buckets)
                {
                    int* ptr = bucketsPtr;
                    var end = bucketsPtr + Buckets.Length;
                    while (ptr < end) *ptr++ = -1;
                }
            }
#else
            for(int i = 0; i < Buckets.Length; i++)
                Buckets[i] = -1;
#endif
        }

        public Pipelines(int prime)
        {
            if (prime < 0 || prime >= Primes.Length) throw new ArgumentException("Capacity Overflow");

            _prime = prime;

            var size = Primes[_prime];
            Buckets = new int[size];
            Entries = new Entry[size];

#if !NET40
            unsafe
            {
                fixed (int* bucketsPtr = Buckets)
                {
                    int* ptr = bucketsPtr;
                    var end = bucketsPtr + Buckets.Length;
                    while (ptr < end) *ptr++ = -1;
                }
            }
#else
            for(int i = 0; i < Buckets.Length; i++)
                Buckets[i] = -1;
#endif
        }

        public Pipelines(Pipelines Pipelines)
            : this(Pipelines._prime + 1)
        {
            Array.Copy(Pipelines.Entries, 0, Entries, 0, Pipelines.Count);
            for (var i = 0; i < Pipelines.Count; i++)
            {
                var hashCode = Entries[i].Key.HashCode;
                if (hashCode < 0) continue;

                var bucket = hashCode % Buckets.Length;
                Entries[i].Next = Buckets[bucket];
                Buckets[bucket] = i;
            }
            Count = Pipelines.Count;
        }

        public Pipelines(BuildPipelineAsync value)
            : this(0)
        {
            ref var entry = ref Entries[0];
            entry.Next = -1;
            entry.Pipeline = value;

            Buckets[0] = 0;
            Count++;
        }

        #endregion


        #region Public Members

        internal void Set(Type type, string? name, BuildPipelineAsync pipeline)
        {
            var key = new HashKey(type, name);
            var targetBucket = key.HashCode % Buckets.Length;

            for (var i = Buckets[targetBucket]; i >= 0; i = Entries[i].Next)
            {
                ref var candidate = ref Entries[i];
                if (candidate.Key != key) continue;

                candidate.Pipeline = pipeline;
                return;
            }

            ref var entry = ref Entries[Count];
            entry.Key = key;
            entry.Next = Buckets[targetBucket];
            entry.Pipeline = pipeline;
            Buckets[targetBucket] = Count++;
        }

        public bool RequireToGrow => (Entries.Length - Count) < 100 &&
                                     (float)Count / Entries.Length > 0.72f;
        #endregion


        #region Entry Type

        public struct Entry
        {
            public HashKey Key;
            public int Next;
            public BuildPipelineAsync Pipeline;
        }

        #endregion


        #region Prime Numbers

        public static readonly int[] Primes = {
            37, 71, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919, 1103, 1327, 1597,
            1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023,
            25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751,
            225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369};

        #endregion
    }
}