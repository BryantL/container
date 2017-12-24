﻿// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Practices.Unity.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Builder;
using Unity.Builder.Strategy;
using Unity.Container;
using Unity.Lifetime;
using Unity.ObjectBuilder;
using Unity.Policy;
using Unity.Strategies;

namespace Unity.Tests.ObjectBuilder
{
    [TestClass]
    public class SingletonThreadingFixture
    {
        [TestMethod]
        public void SingletonReturnsSameValueWhenCalledInMultipleThreads()
        {
            var strategies = GetStrategies();
            IPolicyList policies = GetPolicies();

            BuilderOnThread threadResults1 = new BuilderOnThread(strategies, policies);
            BuilderOnThread threadResults2 = new BuilderOnThread(strategies, policies);

            Task task1 = new Task(threadResults1.Build);
            Task task2 = new Task(threadResults2.Build);

            task1.Start();
            task2.Start();

            Task.WaitAll(task1, task2);

            Assert.AreSame(threadResults1.Result, threadResults2.Result);
        }

        private MockStrategyChain GetStrategies()
        {
            MockStrategyChain chain = new MockStrategyChain();
            chain.Add(new LifetimeStrategy());
            chain.Add(new SleepingStrategy());
            chain.Add(new NewObjectStrategy());
            return chain;
        }

        private IPolicyList GetPolicies()
        {
            PolicyList policies = new PolicyList();
            policies.Set(typeof(object), null, typeof(ILifetimePolicy), new ContainerControlledLifetimeManager());
            return policies;
        }
    }

    // A test class that runs a buildup - designed to be used in a thread
    internal class BuilderOnThread
    {
        public object Result;
        private MockStrategyChain strategies;
        private IPolicyList policies;

        public BuilderOnThread(MockStrategyChain strategies, IPolicyList policies)
        {
            this.strategies = strategies;
            this.policies = policies;
        }

        public void Build()
        {
            var transientPolicies = new PolicyList(policies);
            var context = new BuilderContext(new UnityContainer(), strategies, null, policies, transientPolicies, new NamedTypeBuildKey<object>(), null);
            Result = strategies.ExecuteBuildUp(context);
        }
    }

    // A test strategy that puts a variable sleep time into
    // the strategy chain
    internal class SleepingStrategy : BuilderStrategy
    {
        private int sleepTimeMS = 500;
        private static object @lock = new object();

        public override object PreBuildUp(IBuilderContext context)
        {
            // Sleep
            lock (SleepingStrategy.@lock)
            {
                SpinWait.SpinUntil(() => false, this.sleepTimeMS);
            }

            this.sleepTimeMS = this.sleepTimeMS == 0 ? 500 : 0;
            return null;
        }
    }

    // A test strategy that just creates an object.
    internal class NewObjectStrategy : BuilderStrategy
    {
        public override object PreBuildUp(IBuilderContext context)
        {
            context.Existing = new object();
            return null;
        }
    }
}
