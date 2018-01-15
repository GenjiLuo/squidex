﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Linq;
using GraphQL.Resolvers;
using GraphQL.Types;
using Squidex.Domain.Apps.Entities.Schemas;
using Squidex.Infrastructure;

namespace Squidex.Domain.Apps.Entities.Contents.GraphQL.Types
{
    public sealed class ContentGraphType : ObjectGraphType<IContentEntity>
    {
        private readonly ISchemaEntity schema;
        private readonly IGraphQLContext ctx;

        public ContentGraphType(ISchemaEntity schema, IGraphQLContext ctx)
        {
            this.ctx = ctx;
            this.schema = schema;

            Name = $"{schema.Name.ToPascalCase()}Dto";
        }

        public void Initialize()
        {
            var schemaName = schema.SchemaDef.Properties.Label.WithFallback(schema.Name);

            AddField(new FieldType
            {
                Name = "id",
                Resolver = Resolver(x => x.Id.ToString()),
                ResolvedType = new NonNullGraphType(new StringGraphType()),
                Description = $"The id of the {schemaName} content."
            });

            AddField(new FieldType
            {
                Name = "version",
                Resolver = Resolver(x => x.Version),
                ResolvedType = new NonNullGraphType(new IntGraphType()),
                Description = $"The version of the {schemaName} content."
            });

            AddField(new FieldType
            {
                Name = "created",
                Resolver = Resolver(x => x.Created.ToDateTimeUtc()),
                ResolvedType = new NonNullGraphType(new DateGraphType()),
                Description = $"The date and time when the {schemaName} content has been created."
            });

            AddField(new FieldType
            {
                Name = "createdBy",
                Resolver = Resolver(x => x.CreatedBy.ToString()),
                ResolvedType = new NonNullGraphType(new StringGraphType()),
                Description = $"The user that has created the {schemaName} content."
            });

            AddField(new FieldType
            {
                Name = "lastModified",
                Resolver = Resolver(x => x.LastModified.ToDateTimeUtc()),
                ResolvedType = new NonNullGraphType(new DateGraphType()),
                Description = $"The date and time when the {schemaName} content has been modified last."
            });

            AddField(new FieldType
            {
                Name = "lastModifiedBy",
                Resolver = Resolver(x => x.LastModifiedBy.ToString()),
                ResolvedType = new NonNullGraphType(new StringGraphType()),
                Description = $"The user that has updated the {schemaName} content last."
            });

            AddField(new FieldType
            {
                Name = "url",
                Resolver = ctx.ResolveContentUrl(schema),
                ResolvedType = new NonNullGraphType(new StringGraphType()),
                Description = $"The url to the the {schemaName} content."
            });

            var dataType = new ContentDataGraphType(schema.SchemaDef, ctx);

            if (dataType.Fields.Any())
            {
                AddField(new FieldType
                {
                    Name = "data",
                    Resolver = Resolver(x => x.Data),
                    ResolvedType = new NonNullGraphType(dataType),
                    Description = $"The data of the {schemaName} content."
                });
            }

            Description = $"The structure of a {schemaName} content type.";
        }

        private static IFieldResolver Resolver(Func<IContentEntity, object> action)
        {
            return new FuncFieldResolver<IContentEntity, object>(c => action(c.Source));
        }
    }
}
