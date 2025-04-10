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
        public sealed class LogIdCounterHandle : RemoteTableHandle<EventContext, LogIdCounter>
        {
            protected override string RemoteTableName => "LogIdCounter";

            public sealed class KeyUniqueIndex : UniqueIndexBase<string>
            {
                protected override string GetKey(LogIdCounter row) => row.Key;

                public KeyUniqueIndex(LogIdCounterHandle table) : base(table) { }
            }

            public readonly KeyUniqueIndex Key;

            internal LogIdCounterHandle(DbConnection conn) : base(conn)
            {
                Key = new(this);
            }

            protected override object GetPrimaryKey(LogIdCounter row) => row.Key;
        }

        public readonly LogIdCounterHandle LogIdCounter;
    }
}
