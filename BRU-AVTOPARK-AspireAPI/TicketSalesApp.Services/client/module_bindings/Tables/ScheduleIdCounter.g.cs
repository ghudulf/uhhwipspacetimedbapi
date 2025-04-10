// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.BSATN;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteTables
    {
        public sealed class ScheduleIdCounterHandle : RemoteTableHandle<EventContext, ScheduleIdCounter>
        {
            protected override string RemoteTableName => "ScheduleIdCounter";

            public sealed class KeyUniqueIndex : UniqueIndexBase<string>
            {
                protected override string GetKey(ScheduleIdCounter row) => row.Key;

                public KeyUniqueIndex(ScheduleIdCounterHandle table) : base(table) { }
            }

            public readonly KeyUniqueIndex Key;

            internal ScheduleIdCounterHandle(DbConnection conn) : base(conn)
            {
                Key = new(this);
            }

            protected override object GetPrimaryKey(ScheduleIdCounter row) => row.Key;
        }

        public readonly ScheduleIdCounterHandle ScheduleIdCounter;
    }
}
