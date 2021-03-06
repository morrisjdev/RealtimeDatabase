﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SapphireDb.Attributes;
using SapphireDb.Helper;
using SapphireDb.Internal;
using SapphireDb.Models;
using SapphireDb.Models.Exceptions;

namespace SapphireDb.Command.CreateRange
{
    class CreateRangeCommandHandler : CommandHandlerBase, ICommandHandler<CreateRangeCommand>
    {
        private readonly IServiceProvider serviceProvider;

        public CreateRangeCommandHandler(DbContextAccesor contextAccessor, IServiceProvider serviceProvider)
            : base(contextAccessor)
        {
            this.serviceProvider = serviceProvider;
        }

        public Task<ResponseBase> Handle(HttpInformation context, CreateRangeCommand command,
            ExecutionContext executionContext)
        {
            SapphireDbContext db = GetContext(command.ContextName);
            KeyValuePair<Type, string> property = db.GetType().GetDbSetType(command.CollectionName);

            if (property.Key == null)
            {
                throw new CollectionNotFoundException(command.ContextName, command.CollectionName);
            }

            if (property.Key.GetModelAttributesInfo().DisableCreateAttribute != null)
            {
                throw new OperationDisabledException("Create", command.ContextName, command.CollectionName);
            }

            return Task.FromResult(CreateObjects(command, property, context, db));
        }

        private ResponseBase CreateObjects(CreateRangeCommand command, KeyValuePair<Type, string> property,
            HttpInformation context, SapphireDbContext db)
        {
            object[] newValues = command.Values.Values<JObject>().Select(newValue => newValue.ToObject(property.Key))
                .ToArray();

            List<CreateResponse> results = newValues.Select(value =>
            {
                if (!property.Key.CanCreate(context, value, serviceProvider))
                {
                    throw new UnauthorizedException("The user is not authorized for this action");
                }

                return SetPropertiesAndValidate<CreateEventAttribute>(db, property, value, context, serviceProvider);
            }).ToList();

            db.SaveChanges();

            foreach (CreateResponse createResponse in results)
            {
                if (createResponse.Value != null)
                {
                    property.Key.ExecuteHookMethods<CreateEventAttribute>(ModelStoreEventAttributeBase.EventType.After,
                        createResponse.Value, null, context, serviceProvider, db);   
                }
            }
            
            CreateRangeResponse response = new CreateRangeResponse
            {
                ReferenceId = command.ReferenceId,
                Results = results
            };

            return response;
        }

        public static CreateResponse SetPropertiesAndValidate<TEventAttribute>(SapphireDbContext db,
            KeyValuePair<Type, string> property, object newValue,
            HttpInformation context, IServiceProvider serviceProvider)
            where TEventAttribute : ModelStoreEventAttributeBase
        {
            object newEntityObject = property.Key.SetFields(newValue);

            if (!ValidationHelper.ValidateModel(newEntityObject, serviceProvider,
                out Dictionary<string, List<string>> validationResults))
            {
                return new CreateResponse()
                {
                    Value = newEntityObject,
                    ValidationResults = validationResults
                };
            }

            int insteadOfExecuteCount = property.Key.ExecuteHookMethods<TEventAttribute>(
                ModelStoreEventAttributeBase.EventType.InsteadOf,
                newEntityObject, null, context, serviceProvider, db);

            if (insteadOfExecuteCount > 0)
            {
                return new CreateResponse();
            }
            
            property.Key.ExecuteHookMethods<TEventAttribute>(ModelStoreEventAttributeBase.EventType.Before,
                newEntityObject, null, context, serviceProvider, db);

            db.Add(newEntityObject);

            property.Key.ExecuteHookMethods<TEventAttribute>(ModelStoreEventAttributeBase.EventType.BeforeSave,
                newEntityObject, null, context, serviceProvider, db);

            return new CreateResponse()
            {
                Value = newEntityObject,
            };
        }
    }
}