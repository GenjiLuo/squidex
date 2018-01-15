﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime.Extensions;
using Squidex.Domain.Apps.Core;
using Squidex.Domain.Apps.Core.Apps;
using Squidex.Domain.Apps.Core.Contents;
using Squidex.Domain.Apps.Core.Schemas;
using Squidex.Domain.Apps.Entities.Apps;
using Squidex.Domain.Apps.Entities.Assets;
using Squidex.Domain.Apps.Entities.Assets.Repositories;
using Squidex.Domain.Apps.Entities.Contents.TestData;
using Squidex.Domain.Apps.Entities.Schemas;
using Squidex.Infrastructure;
using Xunit;

#pragma warning disable SA1311 // Static readonly fields must begin with upper-case letter

namespace Squidex.Domain.Apps.Entities.Contents.GraphQL
{
    public class GraphQLTests
    {
        private static readonly Guid schemaId = Guid.NewGuid();
        private static readonly Guid appId = Guid.NewGuid();
        private static readonly string appName = "my-app";
        private readonly Schema schemaDef;
        private readonly IContentQueryService contentQuery = A.Fake<IContentQueryService>();
        private readonly IAssetRepository assetRepository = A.Fake<IAssetRepository>();
        private readonly ISchemaEntity schema = A.Fake<ISchemaEntity>();
        private readonly IMemoryCache cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        private readonly IAppProvider appProvider = A.Fake<IAppProvider>();
        private readonly IAppEntity app = A.Dummy<IAppEntity>();
        private readonly ClaimsPrincipal user = new ClaimsPrincipal();
        private readonly IGraphQLService sut;

        public GraphQLTests()
        {
            schemaDef =
                new Schema("my-schema")
                    .AddField(new JsonField(1, "my-json", Partitioning.Invariant,
                        new JsonFieldProperties()))
                    .AddField(new StringField(2, "my-string", Partitioning.Language,
                        new StringFieldProperties()))
                    .AddField(new NumberField(3, "my-number", Partitioning.Invariant,
                        new NumberFieldProperties()))
                    .AddField(new AssetsField(4, "my-assets", Partitioning.Invariant,
                        new AssetsFieldProperties()))
                    .AddField(new BooleanField(5, "my-boolean", Partitioning.Invariant,
                        new BooleanFieldProperties()))
                    .AddField(new DateTimeField(6, "my-datetime", Partitioning.Invariant,
                        new DateTimeFieldProperties()))
                    .AddField(new ReferencesField(7, "my-references", Partitioning.Invariant,
                        new ReferencesFieldProperties { SchemaId = schemaId }))
                    .AddField(new ReferencesField(9, "my-invalid", Partitioning.Invariant,
                        new ReferencesFieldProperties { SchemaId = Guid.NewGuid() }))
                    .AddField(new GeolocationField(10, "my-geolocation", Partitioning.Invariant,
                        new GeolocationFieldProperties()))
                    .AddField(new TagsField(11, "my-tags", Partitioning.Invariant,
                        new TagsFieldProperties()));

            A.CallTo(() => app.Id).Returns(appId);
            A.CallTo(() => app.Name).Returns(appName);
            A.CallTo(() => app.LanguagesConfig).Returns(LanguagesConfig.Build(Language.DE));

            A.CallTo(() => schema.Id).Returns(schemaId);
            A.CallTo(() => schema.Name).Returns(schemaDef.Name);
            A.CallTo(() => schema.SchemaDef).Returns(schemaDef);
            A.CallTo(() => schema.IsPublished).Returns(true);
            A.CallTo(() => schema.ScriptQuery).Returns("<script-query>");

            var allSchemas = new List<ISchemaEntity> { schema };

            A.CallTo(() => appProvider.GetSchemasAsync(appId)).Returns(allSchemas);

            sut = new CachingGraphQLService(cache, appProvider, assetRepository, contentQuery, new FakeUrlGenerator());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task Should_return_empty_object_for_empty_query(string query)
        {
            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_return_multiple_assets_when_querying_assets()
        {
            const string query = @"
                query {
                  queryAssets(search: ""my-query"", top: 30, skip: 5) {
                    id
                    version
                    created
                    createdBy
                    lastModified
                    lastModifiedBy
                    url
                    thumbnailUrl
                    sourceUrl
                    mimeType
                    fileName
                    fileSize
                    fileVersion
                    isImage
                    pixelWidth
                    pixelHeight
                  }
                }";

            var asset = CreateAsset(Guid.NewGuid());

            var assets = new List<IAssetEntity> { asset };

            A.CallTo(() => assetRepository.QueryAsync(app.Id, null, null, "my-query", 30, 5))
                .Returns(ResultList.Create(assets, 0));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    queryAssets = new dynamic[]
                    {
                        new
                        {
                            id = asset.Id,
                            version = 1,
                            created = asset.Created.ToDateTimeUtc(),
                            createdBy = "subject:user1",
                            lastModified = asset.LastModified.ToDateTimeUtc(),
                            lastModifiedBy = "subject:user2",
                            url = $"assets/{asset.Id}",
                            thumbnailUrl = $"assets/{asset.Id}?width=100",
                            sourceUrl = $"assets/source/{asset.Id}",
                            mimeType = "image/png",
                            fileName = "MyFile.png",
                            fileSize = 1024,
                            fileVersion = 123,
                            isImage = true,
                            pixelWidth = 800,
                            pixelHeight = 600
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_return_multiple_assets_with_total_when_querying_assets_with_total()
        {
            const string query = @"
                query {
                  queryAssetsWithTotal(search: ""my-query"", top: 30, skip: 5) {
                    total
                    items {
                      id
                      version
                      created
                      createdBy
                      lastModified
                      lastModifiedBy
                      url
                      thumbnailUrl
                      sourceUrl
                      mimeType
                      fileName
                      fileSize
                      fileVersion
                      isImage
                      pixelWidth
                      pixelHeight
                    }   
                  }
                }";

            var asset = CreateAsset(Guid.NewGuid());

            var assets = new List<IAssetEntity> { asset };

            A.CallTo(() => assetRepository.QueryAsync(app.Id, null, null, "my-query", 30, 5))
                .Returns(ResultList.Create(assets, 10));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    queryAssetsWithTotal = new
                    {
                        total = 10,
                        items = new dynamic[]
                        {
                            new
                            {
                                id = asset.Id,
                                version = 1,
                                created = asset.Created.ToDateTimeUtc(),
                                createdBy = "subject:user1",
                                lastModified = asset.LastModified.ToDateTimeUtc(),
                                lastModifiedBy = "subject:user2",
                                url = $"assets/{asset.Id}",
                                thumbnailUrl = $"assets/{asset.Id}?width=100",
                                sourceUrl = $"assets/source/{asset.Id}",
                                mimeType = "image/png",
                                fileName = "MyFile.png",
                                fileSize = 1024,
                                fileVersion = 123,
                                isImage = true,
                                pixelWidth = 800,
                                pixelHeight = 600
                            }
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_return_single_asset_when_finding_asset()
        {
            var assetId = Guid.NewGuid();
            var asset = CreateAsset(Guid.NewGuid());

            var query = $@"
                query {{
                  findAsset(id: ""{assetId}"") {{
                    id
                    version
                    created
                    createdBy
                    lastModified
                    lastModifiedBy
                    url
                    thumbnailUrl
                    sourceUrl
                    mimeType
                    fileName
                    fileSize
                    fileVersion
                    isImage
                    pixelWidth
                    pixelHeight
                  }}
                }}";

            A.CallTo(() => assetRepository.FindAssetAsync(assetId))
                .Returns(asset);

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    findAsset = new
                    {
                        id = asset.Id,
                        version = 1,
                        created = asset.Created.ToDateTimeUtc(),
                        createdBy = "subject:user1",
                        lastModified = asset.LastModified.ToDateTimeUtc(),
                        lastModifiedBy = "subject:user2",
                        url = $"assets/{asset.Id}",
                        thumbnailUrl = $"assets/{asset.Id}?width=100",
                        sourceUrl = $"assets/source/{asset.Id}",
                        mimeType = "image/png",
                        fileName = "MyFile.png",
                        fileSize = 1024,
                        fileVersion = 123,
                        isImage = true,
                        pixelWidth = 800,
                        pixelHeight = 600
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_return_multiple_contents_when_querying_contents()
        {
            const string query = @"
                query {
                  queryMySchemaContents(top: 30, skip: 5) {
                    id
                    version
                    created
                    createdBy
                    lastModified
                    lastModifiedBy
                    url
                    data {
                      myString {
                        de
                      }
                      myNumber {
                        iv
                      }
                      myBoolean {
                        iv
                      }
                      myDatetime {
                        iv
                      }
                      myJson {
                        iv
                      }
                      myGeolocation {
                        iv
                      }
                      myTags {
                        iv
                      }
                    }
                  }
                }";

            var content = CreateContent(Guid.NewGuid(), Guid.Empty, Guid.Empty);

            var contents = new List<IContentEntity> { content };

            A.CallTo(() => contentQuery.QueryAsync(app, schema.Id.ToString(), user, false, "?$top=30&$skip=5"))
                .Returns((schema, ResultList.Create(contents, 0)));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    queryMySchemaContents = new dynamic[]
                    {
                        new
                        {
                            id = content.Id,
                            version = 1,
                            created = content.Created.ToDateTimeUtc(),
                            createdBy = "subject:user1",
                            lastModified = content.LastModified.ToDateTimeUtc(),
                            lastModifiedBy = "subject:user2",
                            url = $"contents/my-schema/{content.Id}",
                            data = new
                            {
                                myString = new
                                {
                                    de = "value"
                                },
                                myNumber = new
                                {
                                    iv = 1
                                },
                                myBoolean = new
                                {
                                    iv = true
                                },
                                myDatetime = new
                                {
                                    iv = content.LastModified.ToDateTimeUtc()
                                },
                                myJson = new
                                {
                                    iv = new
                                    {
                                        value = 1
                                    }
                                },
                                myGeolocation = new
                                {
                                    iv = new
                                    {
                                        latitude = 10,
                                        longitude = 20
                                    }
                                },
                                myTags = new
                                {
                                    iv = new[]
                                    {
                                        "tag1",
                                        "tag2"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_return_multiple_contents_with_total_when_querying_contents_with_total()
        {
            const string query = @"
                query {
                  queryMySchemaContentsWithTotal(top: 30, skip: 5) {
                    total
                    items {
                      id
                      version
                      created
                      createdBy
                      lastModified
                      lastModifiedBy
                      url
                      data {
                        myString {
                          de
                        }
                        myNumber {
                          iv
                        }
                        myBoolean {
                          iv
                        }
                        myDatetime {
                          iv
                        }
                        myJson {
                          iv
                        }
                        myGeolocation {
                          iv
                        }
                        myTags {
                          iv
                        }
                      }
                    }
                  }
                }";

            var content = CreateContent(Guid.NewGuid(), Guid.Empty, Guid.Empty);

            var contents = new List<IContentEntity> { content };

            A.CallTo(() => contentQuery.QueryAsync(app, schema.Id.ToString(), user, false, "?$top=30&$skip=5"))
                .Returns((schema, ResultList.Create(contents, 10)));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    queryMySchemaContentsWithTotal = new
                    {
                        total = 10,
                        items = new dynamic[]
                        {
                            new
                            {
                                id = content.Id,
                                version = 1,
                                created = content.Created.ToDateTimeUtc(),
                                createdBy = "subject:user1",
                                lastModified = content.LastModified.ToDateTimeUtc(),
                                lastModifiedBy = "subject:user2",
                                url = $"contents/my-schema/{content.Id}",
                                data = new
                                {
                                    myString = new
                                    {
                                        de = "value"
                                    },
                                    myNumber = new
                                    {
                                        iv = 1
                                    },
                                    myBoolean = new
                                    {
                                        iv = true
                                    },
                                    myDatetime = new
                                    {
                                        iv = content.LastModified.ToDateTimeUtc()
                                    },
                                    myJson = new
                                    {
                                        iv = new
                                        {
                                            value = 1
                                        }
                                    },
                                    myGeolocation = new
                                    {
                                        iv = new
                                        {
                                            latitude = 10,
                                            longitude = 20
                                        }
                                    },
                                    myTags = new
                                    {
                                        iv = new[]
                                        {
                                            "tag1",
                                            "tag2"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_return_single_content_when_finding_content()
        {
            var contentId = Guid.NewGuid();
            var content = CreateContent(contentId, Guid.Empty, Guid.Empty);

            var query = $@"
                query {{
                  findMySchemaContent(id: ""{contentId}"") {{
                    id
                    version
                    created
                    createdBy
                    lastModified
                    lastModifiedBy
                    url
                    data {{
                      myString {{
                        de
                      }}
                      myNumber {{
                        iv
                      }}
                      myBoolean {{
                        iv
                      }}
                      myDatetime {{
                        iv
                      }}
                      myJson {{
                        iv
                      }}
                      myGeolocation {{
                        iv
                      }}
                      myTags {{
                        iv
                      }}
                    }}
                  }}
                }}";

            A.CallTo(() => contentQuery.FindContentAsync(app, schema.Id.ToString(), user, contentId, EtagVersion.Any))
                .Returns((schema, content));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    findMySchemaContent = new
                    {
                        id = content.Id,
                        version = 1,
                        created = content.Created.ToDateTimeUtc(),
                        createdBy = "subject:user1",
                        lastModified = content.LastModified.ToDateTimeUtc(),
                        lastModifiedBy = "subject:user2",
                        url = $"contents/my-schema/{content.Id}",
                        data = new
                        {
                            myString = new
                            {
                                de = "value"
                            },
                            myNumber = new
                            {
                                iv = 1
                            },
                            myBoolean = new
                            {
                                iv = true
                            },
                            myDatetime = new
                            {
                                iv = content.LastModified.ToDateTimeUtc()
                            },
                            myJson = new
                            {
                                iv = new
                                {
                                    value = 1
                                }
                            },
                            myGeolocation = new
                            {
                                iv = new
                                {
                                    latitude = 10,
                                    longitude = 20
                                }
                            },
                            myTags = new
                            {
                                iv = new[]
                                {
                                    "tag1",
                                    "tag2"
                                }
                            }
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_also_fetch_referenced_contents_when_field_is_included_in_query()
        {
            var contentRefId = Guid.NewGuid();
            var contentRef = CreateContent(contentRefId, Guid.Empty, Guid.Empty);

            var contentId = Guid.NewGuid();
            var content = CreateContent(contentId, contentRefId, Guid.Empty);

            var query = $@"
                query {{
                  findMySchemaContent(id: ""{contentId}"") {{
                    id
                    data {{
                      myReferences {{
                        iv {{
                          id
                        }}
                      }}
                    }}
                  }}
                }}";

            var refContents = new List<IContentEntity> { contentRef };

            A.CallTo(() => contentQuery.FindContentAsync(app, schema.Id.ToString(), user, contentId, EtagVersion.Any))
                .Returns((schema, content));

            A.CallTo(() => contentQuery.QueryAsync(app, schema.Id.ToString(), user, false, A<HashSet<Guid>>.That.Matches(x => x.Contains(contentRefId))))
                .Returns((schema, ResultList.Create(refContents, 0)));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    findMySchemaContent = new
                    {
                        id = content.Id,
                        data = new
                        {
                            myReferences = new
                            {
                                iv = new[]
                                {
                                    new
                                    {
                                        id = contentRefId
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_also_fetch_referenced_assets_when_field_is_included_in_query()
        {
            var assetRefId = Guid.NewGuid();
            var assetRef = CreateAsset(assetRefId);

            var contentId = Guid.NewGuid();
            var content = CreateContent(contentId, Guid.Empty, assetRefId);

            var query = $@"
                query {{
                  findMySchemaContent(id: ""{contentId}"") {{
                    id
                    data {{
                      myAssets {{
                        iv {{
                          id
                        }}
                      }}
                    }}
                  }}
                }}";

            var refAssets = new List<IAssetEntity> { assetRef };

            A.CallTo(() => contentQuery.FindContentAsync(app, schema.Id.ToString(), user, contentId, EtagVersion.Any))
                .Returns((schema, content));

            A.CallTo(() => assetRepository.QueryAsync(app.Id, null, A<HashSet<Guid>>.That.Matches(x => x.Contains(assetRefId)), null, int.MaxValue, 0))
                .Returns(ResultList.Create(refAssets, 0));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = new
                {
                    findMySchemaContent = new
                    {
                        id = content.Id,
                        data = new
                        {
                            myAssets = new
                            {
                                iv = new[]
                                {
                                    new
                                    {
                                        id = assetRefId
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AssertJson(expected, new { data = result.Data });
        }

        [Fact]
        public async Task Should_not_return_data_when_field_not_part_of_content()
        {
            var contentId = Guid.NewGuid();
            var content = CreateContent(contentId, Guid.Empty, Guid.Empty, new NamedContentData());

            var query = $@"
                query {{
                  findMySchemaContent(id: ""{contentId}"") {{
                    id
                    version
                    created
                    createdBy
                    lastModified
                    lastModifiedBy
                    url
                    data {{
                      myInvalid {{
                        iv
                      }}
                    }}
                  }}
                }}";

            A.CallTo(() => contentQuery.FindContentAsync(app, schema.Id.ToString(), user, contentId, EtagVersion.Any))
                .Returns((schema, content));

            var result = await sut.QueryAsync(app, user, new GraphQLQuery { Query = query });

            var expected = new
            {
                data = (object)null
            };

            AssertJson(expected, new { data = result.Data });
        }

        private static IContentEntity CreateContent(Guid id, Guid refId, Guid assetId, NamedContentData data = null)
        {
            var now = DateTime.UtcNow.ToInstant();

            data = data ??
                new NamedContentData()
                    .AddField("my-json",
                        new ContentFieldData().AddValue("iv", JToken.FromObject(new { value = 1 })))
                    .AddField("my-string",
                        new ContentFieldData().AddValue("de", "value"))
                    .AddField("my-assets",
                        new ContentFieldData().AddValue("iv", JToken.FromObject(new[] { assetId })))
                    .AddField("my-number",
                        new ContentFieldData().AddValue("iv", 1))
                    .AddField("my-boolean",
                        new ContentFieldData().AddValue("iv", true))
                    .AddField("my-datetime",
                        new ContentFieldData().AddValue("iv", now.ToDateTimeUtc()))
                    .AddField("my-tags",
                        new ContentFieldData().AddValue("iv", JToken.FromObject(new[] { "tag1", "tag2" })))
                    .AddField("my-references",
                        new ContentFieldData().AddValue("iv", JToken.FromObject(new[] { refId })))
                    .AddField("my-geolocation",
                        new ContentFieldData().AddValue("iv", JToken.FromObject(new { latitude = 10, longitude = 20 })));

            var content = new FakeContentEntity
            {
                Id = id,
                Version = 1,
                Created = now,
                CreatedBy = new RefToken("subject", "user1"),
                LastModified = now,
                LastModifiedBy = new RefToken("subject", "user2"),
                Data = data
            };

            return content;
        }

        private static IAssetEntity CreateAsset(Guid id)
        {
            var now = DateTime.UtcNow.ToInstant();

            var asset = new FakeAssetEntity
            {
                Id = id,
                Version = 1,
                Created = now,
                CreatedBy = new RefToken("subject", "user1"),
                LastModified = now,
                LastModifiedBy = new RefToken("subject", "user2"),
                FileName = "MyFile.png",
                FileSize = 1024,
                FileVersion = 123,
                MimeType = "image/png",
                IsImage = true,
                PixelWidth = 800,
                PixelHeight = 600
            };

            return asset;
        }

        private static void AssertJson(object expected, object result)
        {
            var resultJson = JsonConvert.SerializeObject(result, Formatting.Indented);
            var expectJson = JsonConvert.SerializeObject(expected, Formatting.Indented);

            Assert.Equal(expectJson, resultJson);
        }
    }
}
