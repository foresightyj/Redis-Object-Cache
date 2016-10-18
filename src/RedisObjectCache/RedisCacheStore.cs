﻿using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace RedisObjectCache
{
    internal sealed class RedisCacheStore
    {
        private readonly IDatabase _redisDatabase;
        private readonly JsonSerializerSettings _jsonSerializerSettings;

        internal RedisCacheStore(IDatabase redisDatabase)
        {
            _redisDatabase = redisDatabase;

            _jsonSerializerSettings = new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new RedisJsonContractResolver()
            };
        }

        internal object Set(RedisCacheEntry entry)
        {
            var ttl = GetTtl(entry.State);

            var valueJson = JsonConvert.SerializeObject(entry.Value, _jsonSerializerSettings);
            var stateJson = JsonConvert.SerializeObject(entry.State, _jsonSerializerSettings);

            _redisDatabase.StringSet(entry.Key, valueJson, ttl);
            _redisDatabase.StringSet(entry.StateKey, stateJson, ttl);

            return entry.Value;
        }

        internal object Get(string key)
        {
            var redisCacheKey = new RedisCacheKey(key);

            var stateJson = _redisDatabase.StringGet(redisCacheKey.StateKey);
            if (string.IsNullOrEmpty(stateJson))
                return null;

            var valueJson = _redisDatabase.StringGet(redisCacheKey.Key);
            var state = JsonConvert.DeserializeObject<RedisCacheEntryState>(stateJson);

            var value = GetObjectFromString(valueJson, state.TypeName);

            if (state.IsSliding)
            {
                state.UpdateUsage();
                stateJson = JsonConvert.SerializeObject(state, _jsonSerializerSettings);

                var ttl = GetTtl(state);
                _redisDatabase.StringSet(redisCacheKey.StateKey, stateJson, ttl);
                _redisDatabase.KeyExpire(redisCacheKey.Key, ttl);
            }

            return value;
        }

        internal object Remove(string key)
        {
            var redisCacheKey = new RedisCacheKey(key);
            var valueJson = _redisDatabase.StringGet(redisCacheKey.Key);
            if (string.IsNullOrEmpty(valueJson))
                return null;

            var value = JsonConvert.DeserializeObject(valueJson);

            _redisDatabase.KeyDelete(redisCacheKey.Key);
            _redisDatabase.KeyDelete(redisCacheKey.StateKey);

            return value;
        }

        private TimeSpan GetTtl(RedisCacheEntryState state)
        {
            return state.UtcAbsoluteExpiration.Subtract(DateTime.UtcNow);
        }

        private readonly MethodInfo _deserializeMethod = typeof(JsonConvert).GetMethods().FirstOrDefault(m => m.Name == "DeserializeObject" && m.IsGenericMethod);

        private readonly ConcurrentDictionary<string, Func<string, object>> _genericMethods = new ConcurrentDictionary<string, Func<string, object>>();

        private object GetObjectFromString(string json, string typeName)
        {
            Func<string, object> serializer;
            if (!_genericMethods.TryGetValue(typeName, out serializer))
            {
                var t = Type.GetType(typeName);
                var genericMethod = _deserializeMethod.MakeGenericMethod(t);
                serializer = s => genericMethod.Invoke(null, new object[] { s }); // No target, no arguments
                _genericMethods[typeName] = serializer;
            }
            return serializer(json);
        }
    }
}