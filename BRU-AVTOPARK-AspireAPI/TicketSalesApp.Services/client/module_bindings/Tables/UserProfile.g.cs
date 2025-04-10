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
        public sealed class UserProfileHandle : RemoteTableHandle<EventContext, UserProfile>
        {
            protected override string RemoteTableName => "UserProfile";

            public sealed class LegacyUserIdUniqueIndex : UniqueIndexBase<uint>
            {
                protected override uint GetKey(UserProfile row) => row.LegacyUserId;

                public LegacyUserIdUniqueIndex(UserProfileHandle table) : base(table) { }
            }

            public readonly LegacyUserIdUniqueIndex LegacyUserId;

            public sealed class LoginUniqueIndex : UniqueIndexBase<string>
            {
                protected override string GetKey(UserProfile row) => row.Login;

                public LoginUniqueIndex(UserProfileHandle table) : base(table) { }
            }

            public readonly LoginUniqueIndex Login;

            public sealed class UserIdUniqueIndex : UniqueIndexBase<SpacetimeDB.Identity>
            {
                protected override SpacetimeDB.Identity GetKey(UserProfile row) => row.UserId;

                public UserIdUniqueIndex(UserProfileHandle table) : base(table) { }
            }

            public readonly UserIdUniqueIndex UserId;

            internal UserProfileHandle(DbConnection conn) : base(conn)
            {
                LegacyUserId = new(this);
                Login = new(this);
                UserId = new(this);
            }

            protected override object GetPrimaryKey(UserProfile row) => row.UserId;
        }

        public readonly UserProfileHandle UserProfile;
    }
}
