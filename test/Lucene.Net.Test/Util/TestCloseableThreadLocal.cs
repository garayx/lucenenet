/* 
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using Lucene.Net.Store;
using NUnit.Framework;

namespace Lucene.Net.Util
{
	
    [TestFixture]
	public class TestCloseableThreadLocal:LuceneTestCase
	{
		public const System.String TEST_VALUE = "initvaluetest";
		
        [Test]
		public virtual void  TestInitValue()
		{
            LightWeightThreadLocal<object> tl = new LightWeightThreadLocal<object>(_=>TEST_VALUE);
			System.String str = (System.String) tl.Get(null);
			Assert.AreEqual(TEST_VALUE, str);
		}
		
        [Test]
		public virtual void  TestNullValue()
		{
            // Tests that null can be set as a valid value (LUCENE-1805). This
            // previously failed in get().
            LightWeightThreadLocal<object> ctl = new LightWeightThreadLocal<object>();
			ctl.Set(null);
			Assert.IsNull(ctl.Get(null));
		}
		
        [Test]
		public virtual void  TestDefaultValueWithoutSetting()
		{
			// LUCENE-1805: make sure default get returns null,
			// twice in a row
            LightWeightThreadLocal<object> ctl = new LightWeightThreadLocal<object>(_=>TEST_VALUE);
			Assert.IsNull(ctl.Get(null));
			Assert.IsNull(ctl.Get(null));
		}
	}
}