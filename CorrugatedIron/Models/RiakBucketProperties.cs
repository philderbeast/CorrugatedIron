﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CorrugatedIron.Containers;
using CorrugatedIron.Extensions;
using CorrugatedIron.Messages;
using CorrugatedIron.Models.CommitHook;
using CorrugatedIron.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CorrugatedIron.Models
{
    // TODO: handle pre/post commit hooks
    public class RiakBucketProperties
    {
        // At the moment, only the NVal and AllowMult can be set via the PBC
        // so if the request has any other value set, we can't use that interface.
        // We check those values and if they're missing we go with PBC as it's
        // substantially quicker.
        public bool CanUsePbc
        {
            get
            {
                return !LastWriteWins.HasValue
                    && RVal == null
                    && RwVal == null
                    && DwVal == null
                    && WVal == null
                    && string.IsNullOrEmpty(Backend);
            }
        }

        public bool? LastWriteWins { get; private set; }
        public uint? NVal { get; private set; }
        public bool? AllowMultiple { get; private set; }
        public string Backend { get; private set; }
        public List<IRiakPreCommitHook> PreCommitHooks { get; private set; }
        public List<IRiakPostCommitHook> PostCommitHooks { get; private set; }

        public Either<uint, string> RVal { get; private set; }
        public Either<uint, string> RwVal { get; private set; }
        public Either<uint, string> DwVal { get; private set; }
        public Either<uint, string> WVal { get; private set; }

        public RiakBucketProperties SetAllowMultiple(bool value)
        {
            AllowMultiple = value;
            return this;
        }

        public RiakBucketProperties SetLastWriteWins(bool value)
        {
            LastWriteWins = value;
            return this;
        }

        public RiakBucketProperties SetNVal(uint value)
        {
            NVal = value;
            return this;
        }

        public RiakBucketProperties SetRVal(uint value)
        {
            return WriteQuorum(value, v => RVal = v);
        }

        public RiakBucketProperties SetRVal(string value)
        {
            return WriteQuorum(value, v => RVal = v);
        }

        public RiakBucketProperties SetRwVal(uint value)
        {
            return WriteQuorum(value, v => RwVal = v);
        }

        public RiakBucketProperties SetRwVal(string value)
        {
            return WriteQuorum(value, v => RwVal = v);
        }

        public RiakBucketProperties SetDwVal(uint value)
        {
            return WriteQuorum(value, v => DwVal = v);
        }

        public RiakBucketProperties SetDwVal(string value)
        {
            return WriteQuorum(value, v => DwVal = v);
        }

        public RiakBucketProperties SetWVal(uint value)
        {
            return WriteQuorum(value, v => WVal = v);
        }

        public RiakBucketProperties SetWVal(string value)
        {
            return WriteQuorum(value, v => WVal = v);
        }

        public RiakBucketProperties SetBackend(string backend)
        {
            Backend = backend;
            return this;
        }

        public RiakBucketProperties AddPreCommitHook(IRiakPreCommitHook commitHook)
        {
            (PreCommitHooks ?? (PreCommitHooks = new List<IRiakPreCommitHook>())).Add(commitHook);
            return this;
        }

        public RiakBucketProperties AddPostCommitHook(IRiakPostCommitHook commitHook)
        {
            (PostCommitHooks ?? (PostCommitHooks = new List<IRiakPostCommitHook>())).Add(commitHook);
            return this;
        }

        public RiakBucketProperties ClearPreCommitHooks()
        {
            (PreCommitHooks ?? (PreCommitHooks = new List<IRiakPreCommitHook>())).Clear();
            return this;
        }

        public RiakBucketProperties ClearPostCommitHooks()
        {
            (PostCommitHooks ?? (PostCommitHooks = new List<IRiakPostCommitHook>())).Clear();
            return this;
        }

        private RiakBucketProperties WriteQuorum(uint value, Action<Either<uint, string>> setter)
        {
            System.Diagnostics.Debug.Assert(value >= 1);
            setter(new Either<uint, string>(value));
            return this;
        }

        private RiakBucketProperties WriteQuorum(string value, Action<Either<uint, string>> setter)
        {
            System.Diagnostics.Debug.Assert(new HashSet<string> { "all", "quorum", "one" }.Contains(value), "Incorrect quorum value");
            setter(new Either<uint, string>(value));
            return this;
        }

        public RiakBucketProperties()
        {
        }

        public RiakBucketProperties(RiakRestResponse response)
        {
            System.Diagnostics.Debug.Assert(response.ContentType == Constants.ContentTypes.ApplicationJson);

            var json = JObject.Parse(response.Body);
            var props = (JObject)json["props"];
            NVal = props.Value<uint?>("n_val");
            AllowMultiple = props.Value<bool?>("allow_mult");
            LastWriteWins = props.Value<bool?>("last_write_wins");
            Backend = props.Value<string>("backend");

            ReadQuorum(props, "r", v => RVal = v);
            ReadQuorum(props, "rw", v => RwVal = v);
            ReadQuorum(props, "dw", v => DwVal = v);
            ReadQuorum(props, "w", v => WVal = v);

            var preCommitHooks = props.Value<JArray>("precommit");
            if (preCommitHooks.Count > 0)
            {
                PreCommitHooks = preCommitHooks.Cast<JObject>().Select(LoadPreCommitHook).ToList();
            }

            var postCommitHooks = props.Value<JArray>("postcommit");
            if (postCommitHooks.Count > 0)
            {
                PostCommitHooks = postCommitHooks.Cast<JObject>().Select(LoadPostCommitHook).ToList();
            }
        }

        private static IRiakPreCommitHook LoadPreCommitHook(JObject hook)
        {
            JToken token;
            if (hook.TryGetValue("name", out token))
            {
                // must be a javascript hook
                return new RiakJavascriptCommitHook(token.Value<string>());
            }

            // otherwise it has to be erlang
            return new RiakErlangCommitHook(hook.Value<string>("mod"), hook.Value<string>("fun"));
        }

        private static IRiakPostCommitHook LoadPostCommitHook(JObject hook)
        {
            // only erlang hooks are supported
            return new RiakErlangCommitHook(hook.Value<string>("mod"), hook.Value<string>("fun"));
        }

        private static void ReadQuorum(JObject props, string key, Action<Either<uint, string>> setter)
        {
            if (props[key].Type == JTokenType.String)
            {
                setter(new Either<uint, string>(props.Value<string>(key)));
            }
            else
            {
                setter(new Either<uint, string>(props.Value<uint>(key)));
            }
        }

        internal RiakBucketProperties(RpbBucketProps bucketProps)
            : this()
        {
            AllowMultiple = bucketProps.AllowMultiple;
            NVal = bucketProps.NVal;
        }

        internal RpbBucketProps ToMessage()
        {
            var message = new RpbBucketProps();
            if(AllowMultiple.HasValue)
            {
                message.AllowMultiple = AllowMultiple.Value;
            }
            if (NVal.HasValue)
            {
                message.NVal = NVal.Value;
            }
            return message;
        }

        internal string ToJsonString()
        {
            var sb = new StringBuilder();
            
            using(var sw = new StringWriter(sb))
            using (JsonWriter jw = new JsonTextWriter(sw))
            {
                jw.WriteStartObject();
                jw.WritePropertyName("props");
                jw.WriteStartObject();
                jw.WriteNullableProperty("n_val", NVal)
                    .WriteNullableProperty("allow_mult", AllowMultiple)
                    .WriteNullableProperty("last_write_wins", LastWriteWins)
                    .WriteEither("r", RVal)
                    .WriteEither("rw", RwVal)
                    .WriteEither("dw", DwVal)
                    .WriteEither("w", WVal)
                    .WriteNonNullProperty("backend", Backend);

                if (PreCommitHooks != null)
                {
                    jw.WritePropertyName("precommit");
                    jw.WriteStartArray();
                    PreCommitHooks.ForEach(hook => hook.WriteJson(jw));
                    jw.WriteEndArray();
                }

                if (PostCommitHooks != null)
                {
                    jw.WritePropertyName("postcommit");
                    jw.WriteStartArray();
                    PostCommitHooks.ForEach(hook => hook.WriteJson(jw));
                    jw.WriteEndArray();
                }

                jw.WriteEndObject();
                jw.WriteEndObject();
            }
            
            return sb.ToString();
        }
    }
}