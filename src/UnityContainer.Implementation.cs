﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unity.Builder;
using Unity.Container;
using Unity.Container.Lifetime;
using Unity.Container.Registration;
using Unity.Events;
using Unity.Exceptions;
using Unity.Extension;
using Unity.Lifetime;
using Unity.ObjectBuilder;
using Unity.ObjectBuilder.BuildPlan;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod.Creation;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod.Method;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod.Property;
using Unity.ObjectBuilder.BuildPlan.Selection;
using Unity.ObjectBuilder.Policies;
using Unity.ObjectBuilder.Strategies;
using Unity.Policy;
using Unity.Resolution;
using Unity.Strategy;

namespace Unity
{
    public partial class UnityContainer
    {
        #region Fields

        private PolicyList _policies;
        private readonly UnityContainer _parent;
        private NamedTypesRegistry _registeredNames;
        private LifetimeContainer _lifetimeContainer;
        private List<UnityContainerExtension> _extensions;
        private StagedStrategyChain<UnityBuildStage> _strategies;
        private StagedStrategyChain<UnityBuildStage> _buildPlanStrategies;

        private event EventHandler<RegisterEventArgs> Registering;
        private event EventHandler<RegisterInstanceEventArgs> RegisteringInstance;
        private event EventHandler<ChildContainerCreatedEventArgs> ChildContainerCreated;

        #endregion


        #region Constructors

        /// <summary>
        /// Create a <see cref="Unity.UnityContainer"/> with the given parent container.
        /// </summary>
        /// <param name="parent">The parent <see cref="Unity.UnityContainer"/>. The current object
        /// will apply its own settings first, and then check the parent for additional ones.</param>
        private UnityContainer(UnityContainer parent)
        {
            _extensions = new List<UnityContainerExtension>();

            _parent = parent;
            _parent?._lifetimeContainer.Add(this);

            _strategies = new StagedStrategyChain<UnityBuildStage>(_parent?._strategies);
            _buildPlanStrategies = new StagedStrategyChain<UnityBuildStage>(_parent?._buildPlanStrategies);
            _registeredNames = new NamedTypesRegistry(_parent?._registeredNames);
            _lifetimeContainer = new LifetimeContainer { _strategies, _buildPlanStrategies };
            _policies = new PolicyList(_parent?._policies);
            _policies.Set<IRegisteredNamesPolicy>(new RegisteredNamesPolicy(_registeredNames), null);

            if (null == _parent) InitializeStrategies();

            RegisterInstance(typeof(IUnityContainer), null, this, new ContainerLifetimeManager());
        }

        #endregion


        #region Default Strategies

        protected void InitializeStrategies()
        {
            // Main strategy chain
            _strategies.AddNew<BuildKeyMappingStrategy>(UnityBuildStage.TypeMapping);
            _strategies.AddNew<HierarchicalLifetimeStrategy>(UnityBuildStage.Lifetime);
            _strategies.AddNew<LifetimeStrategy>(UnityBuildStage.Lifetime);

            _strategies.AddNew<ArrayResolutionStrategy>(UnityBuildStage.Creation);
            _strategies.AddNew<BuildPlanStrategy>(UnityBuildStage.Creation);

            // Build plan strategy chain
            _buildPlanStrategies.AddNew<DynamicMethodConstructorStrategy>(UnityBuildStage.Creation);
            _buildPlanStrategies.AddNew<DynamicMethodPropertySetterStrategy>(UnityBuildStage.Initialization);
            _buildPlanStrategies.AddNew<DynamicMethodCallStrategy>(UnityBuildStage.Initialization);

            // Policies - mostly used by the build plan strategies
            _policies.SetDefault<IConstructorSelectorPolicy>(new DefaultUnityConstructorSelectorPolicy());
            _policies.SetDefault<IPropertySelectorPolicy>(new DefaultUnityPropertySelectorPolicy());
            _policies.SetDefault<IMethodSelectorPolicy>(new DefaultUnityMethodSelectorPolicy());
            _policies.SetDefault<IBuildPlanCreatorPolicy>(new DynamicMethodBuildPlanCreatorPolicy(_buildPlanStrategies));

            _policies.Set<IBuildPlanPolicy>(new DeferredResolveBuildPlanPolicy(), typeof(Func<>));
            _policies.Set<ILifetimePolicy>(new PerResolveLifetimeManager(), typeof(Func<>));
            _policies.Set<IBuildPlanCreatorPolicy>(new LazyDynamicMethodBuildPlanCreatorPolicy(), typeof(Lazy<>));
            _policies.Set<IBuildPlanCreatorPolicy>(new EnumerableDynamicMethodBuildPlanCreatorPolicy(), typeof(IEnumerable<>));
        }


        private void SetLifetimeManager(Type lifetimeType, string name, LifetimeManager lifetimeManager)
        {
            if (lifetimeManager.InUse)
            {
                throw new InvalidOperationException(Constants.LifetimeManagerInUse);
            }

            if (lifetimeType.GetTypeInfo().IsGenericTypeDefinition)
            {
                LifetimeManagerFactory factory =
                    new LifetimeManagerFactory(new ContainerContext(this), lifetimeManager.GetType());
                _policies.Set<ILifetimeFactoryPolicy>(factory,
                    new NamedTypeBuildKey(lifetimeType, name));
            }
            else
            {
                lifetimeManager.InUse = true;
                _policies.Set<ILifetimePolicy>(lifetimeManager,
                    new NamedTypeBuildKey(lifetimeType, name));
                if (lifetimeManager is IDisposable)
                {
                    _lifetimeContainer.Add(lifetimeManager);
                }
            }
        }

        #endregion



        #region Implementation

        private object BuildUp(NamedTypeBuildKey key, object existing, params ResolverOverride[] resolverOverrides)
        {
            IBuilderContext context = null;

            try
            {
                context = new BuilderContext(this, _strategies.MakeStrategyChain(),
                                                   _lifetimeContainer,
                                                   _policies,
                                                   key,
                                                   existing);

                if (null != resolverOverrides && 0 != resolverOverrides.Length)
                    context.AddResolverOverrides(resolverOverrides);

                if (key.Type.GetTypeInfo().IsGenericTypeDefinition)
                {
                    throw new ArgumentException(
                        String.Format(CultureInfo.CurrentCulture,
                            Constants.CannotResolveOpenGenericType,
                            key.Type.FullName), nameof(key.Type));
                }

                return context.Strategies.ExecuteBuildUp(context);
            }
            catch (Exception ex)
            {
                throw new ResolutionFailedException(key.Type, key.Name, ex, context);
            }
        }

        private static void InstanceIsAssignable(Type assignmentTargetType, object assignmentInstance, string argumentName)
        {
            if (!(assignmentTargetType ?? throw new ArgumentNullException(nameof(assignmentTargetType)))
                .GetTypeInfo().IsAssignableFrom((assignmentInstance ?? throw new ArgumentNullException(nameof(assignmentInstance))).GetType().GetTypeInfo()))
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Constants.TypesAreNotAssignable,
                        assignmentTargetType, GetTypeName(assignmentInstance)),
                    argumentName);
            }
        }

        private static string GetTypeName(object assignmentInstance)
        {
            string assignmentInstanceType;
            try
            {
                assignmentInstanceType = assignmentInstance.GetType().FullName;
            }
            catch (Exception)
            {
                assignmentInstanceType = Constants.UnknownType;
            }

            return assignmentInstanceType;
        }

        #endregion


        #region Nested Types

        /// <summary>
        /// Implementation of the ExtensionContext that is actually used
        /// by the UnityContainer implementation.
        /// </summary>
        /// <remarks>
        /// This is a nested class so that it can access state in the
        /// container that would otherwise be inaccessible.
        /// </remarks>
        private class ContainerContext : ExtensionContext
        {
            private readonly UnityContainer _container;

            public ContainerContext(UnityContainer container)
            {
                _container = container ?? throw new ArgumentNullException(nameof(container));
            }

            public override IUnityContainer Container => _container;

            public override IStagedStrategyChain<UnityBuildStage> Strategies => _container._strategies;

            public override IStagedStrategyChain<UnityBuildStage> BuildPlanStrategies => _container._buildPlanStrategies;

            public override IPolicyList Policies => _container._policies;

            public override ILifetimeContainer Lifetime => _container._lifetimeContainer;

            public override event EventHandler<RegisterEventArgs> Registering
            {
                add => _container.Registering += value;
                remove => _container.Registering -= value;
            }

            /// <summary>
            /// This event is raised when the <see cref="RegisterInstance(Type,string,object,LifetimeManager)"/> method,
            /// or one of its overloads, is called.
            /// </summary>
            public override event EventHandler<RegisterInstanceEventArgs> RegisteringInstance
            {
                add => _container.RegisteringInstance += value;
                remove => _container.RegisteringInstance -= value;
            }

            public override event EventHandler<ChildContainerCreatedEventArgs> ChildContainerCreated
            {
                add => _container.ChildContainerCreated += value;
                remove => _container.ChildContainerCreated -= value;
            }
        }


        // Works like the ExternallyControlledLifetimeManager, but uses regular instead of weak references
        private class ContainerLifetimeManager : LifetimeManager, IResolverPolicy
        {
            private object _value;

            public override object GetValue()
            {
                return _value;
            }

            public override void SetValue(object newValue)
            {
                _value = newValue;
            }

            public override void RemoveValue()
            {
            }

            public object Resolve(IBuilderContext _)
            {
                return _value;
            }
        }

        #endregion
    }
}
