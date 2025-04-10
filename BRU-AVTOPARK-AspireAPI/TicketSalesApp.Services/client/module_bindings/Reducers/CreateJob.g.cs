// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteReducers : RemoteBase
    {
        public delegate void CreateJobHandler(ReducerEventContext ctx, string jobTitle, string jobInternship);
        public event CreateJobHandler? OnCreateJob;

        public void CreateJob(string jobTitle, string jobInternship)
        {
            conn.InternalCallReducer(new Reducer.CreateJob(jobTitle, jobInternship), this.SetCallReducerFlags.CreateJobFlags);
        }

        public bool InvokeCreateJob(ReducerEventContext ctx, Reducer.CreateJob args)
        {
            if (OnCreateJob == null) return false;
            OnCreateJob(
                ctx,
                args.JobTitle,
                args.JobInternship
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class CreateJob : Reducer, IReducerArgs
        {
            [DataMember(Name = "jobTitle")]
            public string JobTitle;
            [DataMember(Name = "jobInternship")]
            public string JobInternship;

            public CreateJob(
                string JobTitle,
                string JobInternship
            )
            {
                this.JobTitle = JobTitle;
                this.JobInternship = JobInternship;
            }

            public CreateJob()
            {
                this.JobTitle = "";
                this.JobInternship = "";
            }

            string IReducerArgs.ReducerName => "CreateJob";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags CreateJobFlags;
        public void CreateJob(CallReducerFlags flags) => CreateJobFlags = flags;
    }
}
