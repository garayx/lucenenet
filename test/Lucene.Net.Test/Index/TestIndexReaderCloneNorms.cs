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

using Analyzer = Lucene.Net.Analysis.Analyzer;
using StandardAnalyzer = Lucene.Net.Analysis.Standard.StandardAnalyzer;
using Document = Lucene.Net.Documents.Document;
using Field = Lucene.Net.Documents.Field;
using Index = Lucene.Net.Documents.Field.Index;
using Store = Lucene.Net.Documents.Field.Store;
using Norm = Lucene.Net.Index.SegmentReader.Norm;
using Directory = Lucene.Net.Store.Directory;
using FSDirectory = Lucene.Net.Store.FSDirectory;
using MockRAMDirectory = Lucene.Net.Store.MockRAMDirectory;
using DefaultSimilarity = Lucene.Net.Search.DefaultSimilarity;
using Similarity = Lucene.Net.Search.Similarity;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

namespace Lucene.Net.Index
{
	
	/// <summary> Tests cloning IndexReader norms</summary>
    [TestFixture]
	public class TestIndexReaderCloneNorms:LuceneTestCase
	{
		
		[Serializable]
		private class SimilarityOne:DefaultSimilarity
		{
			public SimilarityOne(TestIndexReaderCloneNorms enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestIndexReaderCloneNorms enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestIndexReaderCloneNorms enclosingInstance;
			public TestIndexReaderCloneNorms Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			public override float LengthNorm(System.String fieldName, int numTerms)
			{
				return 1;
			}
		}
		
		private const int NUM_FIELDS = 10;
		
		private Similarity similarityOne;
		
		private Analyzer anlzr;
		
		private int numDocNorms;
		
		private System.Collections.ArrayList norms;
		
		private System.Collections.ArrayList modifiedNorms;
		
		private float lastNorm = 0;
		
		private float normDelta = (float) 0.001;
		
		public TestIndexReaderCloneNorms(System.String s):base(s)
		{
		}

        public TestIndexReaderCloneNorms(): base("")
        {
        }
		
		[SetUp]
		public override void  SetUp()
		{
			base.SetUp();
			similarityOne = new SimilarityOne(this);
			anlzr = new StandardAnalyzer(Util.Version.LUCENE_CURRENT);
		}
		
		/// <summary> Test that norms values are preserved as the index is maintained. Including
		/// separate norms. Including merging indexes with seprate norms. Including
		/// optimize.
		/// </summary>
        [Test]
		public virtual void  TestNorms()
		{
			// tmp dir
			System.String tempDir = System.IO.Path.GetTempPath();
			if (tempDir == null)
			{
				throw new System.IO.IOException("java.io.tmpdir undefined, cannot run test");
			}
			
			// test with a single index: index1
			System.IO.DirectoryInfo indexDir1 = new System.IO.DirectoryInfo(System.IO.Path.Combine(tempDir, "lucenetestindex1"));
			Directory dir1 = FSDirectory.Open(indexDir1);
			IndexWriter.Unlock(dir1);
			
			norms = new System.Collections.ArrayList();
			modifiedNorms = new System.Collections.ArrayList();
			
			CreateIndex(dir1);
			DoTestNorms(dir1);
			
			// test with a single index: index2
			System.Collections.ArrayList norms1 = norms;
			System.Collections.ArrayList modifiedNorms1 = modifiedNorms;
			int numDocNorms1 = numDocNorms;
			
			norms = new System.Collections.ArrayList();
			modifiedNorms = new System.Collections.ArrayList();
			numDocNorms = 0;
			
			System.IO.DirectoryInfo indexDir2 = new System.IO.DirectoryInfo(System.IO.Path.Combine(tempDir, "lucenetestindex2"));
			Directory dir2 = FSDirectory.Open(indexDir2);
			
			CreateIndex(dir2);
			DoTestNorms(dir2);
			
			// add index1 and index2 to a third index: index3
			System.IO.DirectoryInfo indexDir3 = new System.IO.DirectoryInfo(System.IO.Path.Combine(tempDir, "lucenetestindex3"));
			Directory dir3 = FSDirectory.Open(indexDir3);
			
			CreateIndex(dir3);
			IndexWriter iw = new IndexWriter(dir3, anlzr, false, IndexWriter.MaxFieldLength.LIMITED, null);
			iw.SetMaxBufferedDocs(5);
			iw.MergeFactor = 3;
			iw.AddIndexesNoOptimize(null, new Directory[]{dir1, dir2});
            iw.Optimize(null);
			iw.Close();
			
			norms1.AddRange(norms);
			norms = norms1;
			modifiedNorms1.AddRange(modifiedNorms);
			modifiedNorms = modifiedNorms1;
			numDocNorms += numDocNorms1;
			
			// test with index3
			VerifyIndex(dir3);
			DoTestNorms(dir3);
			
			// now with optimize
			iw = new IndexWriter(dir3, anlzr, false, IndexWriter.MaxFieldLength.LIMITED, null);
			iw.SetMaxBufferedDocs(5);
			iw.MergeFactor = 3;
			iw.Optimize(null);
			iw.Close();
			VerifyIndex(dir3);
			
			dir1.Close();
			dir2.Close();
			dir3.Close();
		}
		
		// try cloning and reopening the norms
		private void  DoTestNorms(Directory dir)
		{
			AddDocs(dir, 12, true);
			IndexReader ir = IndexReader.Open(dir, false,  null);
			VerifyIndex(ir);
			ModifyNormsForF1(ir);
			IndexReader irc = (IndexReader) ir.Clone(null); // IndexReader.open(dir, false);//ir.clone();
			VerifyIndex(irc);
			
			ModifyNormsForF1(irc);
			
			IndexReader irc3 = (IndexReader) irc.Clone(null);
			VerifyIndex(irc3);
			ModifyNormsForF1(irc3);
			VerifyIndex(irc3);
			irc3.Flush(null);
			irc3.Close();
		}
		
        [Test]
		public virtual void  TestNormsClose()
		{
			Directory dir1 = new MockRAMDirectory();
			TestIndexReaderReopen.CreateIndex(dir1, false);
			SegmentReader reader1 = SegmentReader.GetOnlySegmentReader(dir1, null);
			reader1.Norms("field1", null);
			Norm r1norm = reader1.norms_ForNUnit["field1"];
			SegmentReader.Ref r1BytesRef = r1norm.BytesRef();
			SegmentReader reader2 = (SegmentReader) reader1.Clone(null);
			Assert.AreEqual(2, r1norm.BytesRef().RefCount());
			reader1.Close();
			Assert.AreEqual(1, r1BytesRef.RefCount());
			reader2.Norms("field1", null);
			reader2.Close();
			dir1.Close();
		}
		
        [Test]
		public virtual void  TestNormsRefCounting()
		{
			Directory dir1 = new MockRAMDirectory();
			TestIndexReaderReopen.CreateIndex(dir1, false);
            IndexReader reader1 = IndexReader.Open(dir1, false,  null);
			
			IndexReader reader2C = (IndexReader) reader1.Clone(null);
			SegmentReader segmentReader2C = SegmentReader.GetOnlySegmentReader(reader2C);
			segmentReader2C.Norms("field1", null); // load the norms for the field
			Norm reader2CNorm = segmentReader2C.norms_ForNUnit["field1"];
			Assert.IsTrue(reader2CNorm.BytesRef().RefCount() == 2, "reader2CNorm.bytesRef()=" + reader2CNorm.BytesRef());
			
			
			
			IndexReader reader3C = (IndexReader) reader2C.Clone(null);
			SegmentReader segmentReader3C = SegmentReader.GetOnlySegmentReader(reader3C);
			Norm reader3CCNorm = segmentReader3C.norms_ForNUnit["field1"];
			Assert.AreEqual(3, reader3CCNorm.BytesRef().RefCount());
			
			// edit a norm and the refcount should be 1
			IndexReader reader4C = (IndexReader) reader3C.Clone(null);
			SegmentReader segmentReader4C = SegmentReader.GetOnlySegmentReader(reader4C);
			Assert.AreEqual(4, reader3CCNorm.BytesRef().RefCount());
			reader4C.SetNorm(5, "field1", 0.33f, null);
			
			// generate a cannot update exception in reader1
            Assert.Throws<LockObtainFailedException>(() => reader3C.SetNorm(1, "field1", 0.99f, null), "did not hit expected exception");
			
			// norm values should be different 
			Assert.IsTrue(Similarity.DecodeNorm(segmentReader3C.Norms("field1", null)[5]) != Similarity.DecodeNorm(segmentReader4C.Norms("field1", null)[5]));
			Norm reader4CCNorm = segmentReader4C.norms_ForNUnit["field1"];
			Assert.AreEqual(3, reader3CCNorm.BytesRef().RefCount());
			Assert.AreEqual(1, reader4CCNorm.BytesRef().RefCount());
			
			IndexReader reader5C = (IndexReader) reader4C.Clone(null);
			SegmentReader segmentReader5C = SegmentReader.GetOnlySegmentReader(reader5C);
			Norm reader5CCNorm = segmentReader5C.norms_ForNUnit["field1"];
			reader5C.SetNorm(5, "field1", 0.7f, null);
			Assert.AreEqual(1, reader5CCNorm.BytesRef().RefCount());
			
			reader5C.Close();
			reader4C.Close();
			reader3C.Close();
			reader2C.Close();
			reader1.Close();
			dir1.Close();
		}
		
		private void  CreateIndex(Directory dir)
		{
			IndexWriter iw = new IndexWriter(dir, anlzr, true, IndexWriter.MaxFieldLength.LIMITED, null);
			iw.SetMaxBufferedDocs(5);
			iw.MergeFactor = 3;
			iw.SetSimilarity(similarityOne);
			iw.UseCompoundFile = true;
			iw.Close();
		}
		
		private void  ModifyNormsForF1(Directory dir)
		{
            IndexReader ir = IndexReader.Open(dir, false,  null);
			ModifyNormsForF1(ir);
		}
		
		private void  ModifyNormsForF1(IndexReader ir)
		{
			int n = ir.MaxDoc;
			// System.out.println("modifyNormsForF1 maxDoc: "+n);
			for (int i = 0; i < n; i += 3)
			{
				// modify for every third doc
				int k = (i * 3) % modifiedNorms.Count;
				float origNorm = (float) (modifiedNorms[i]);
				float newNorm = (float) (modifiedNorms[k]);
				// System.out.println("Modifying: for "+i+" from "+origNorm+" to
				// "+newNorm);
				// System.out.println(" and: for "+k+" from "+newNorm+" to "+origNorm);
				modifiedNorms[i] = newNorm;
				modifiedNorms[k] = origNorm;
				ir.SetNorm(i, "f" + 1, newNorm, null);
				ir.SetNorm(k, "f" + 1, origNorm, null);
				// System.out.println("setNorm i: "+i);
				// break;
			}
			// ir.close();
		}
		
		private void  VerifyIndex(Directory dir)
		{
            IndexReader ir = IndexReader.Open(dir, false,  null);
			VerifyIndex(ir);
			ir.Close();
		}
		
		private void  VerifyIndex(IndexReader ir)
		{
			for (int i = 0; i < NUM_FIELDS; i++)
			{
				System.String field = "f" + i;
				byte[] b = ir.Norms(field, null);
				Assert.AreEqual(numDocNorms, b.Length, "number of norms mismatches");
				System.Collections.ArrayList storedNorms = (i == 1?modifiedNorms:norms);
				for (int j = 0; j < b.Length; j++)
				{
					float norm = Similarity.DecodeNorm(b[j]);
					float norm1 = (float) storedNorms[j];
					Assert.AreEqual(norm, norm1, 0.000001, "stored norm value of " + field + " for doc " + j + " is " + norm + " - a mismatch!");
				}
			}
		}
		
		private void  AddDocs(Directory dir, int ndocs, bool compound)
		{
			IndexWriter iw = new IndexWriter(dir, anlzr, false, IndexWriter.MaxFieldLength.LIMITED, null);
			iw.SetMaxBufferedDocs(5);
			iw.MergeFactor = 3;
			iw.SetSimilarity(similarityOne);
			iw.UseCompoundFile = compound;
			for (int i = 0; i < ndocs; i++)
			{
				iw.AddDocument(NewDoc(), null);
			}
			iw.Close();
		}
		
		// create the next document
		private Document NewDoc()
		{
			Document d = new Document();
			float boost = NextNorm();
			for (int i = 0; i < 10; i++)
			{
				Field f = new Field("f" + i, "v" + i, Field.Store.NO, Field.Index.NOT_ANALYZED);
				f.Boost = boost;
				d.Add(f);
			}
			return d;
		}
		
		// return unique norm values that are unchanged by encoding/decoding
		private float NextNorm()
		{
			float norm = lastNorm + normDelta;
			do 
			{
				float norm1 = Similarity.DecodeNorm(Similarity.EncodeNorm(norm));
				if (norm1 > lastNorm)
				{
					// System.out.println(norm1+" > "+lastNorm);
					norm = norm1;
					break;
				}
				norm += normDelta;
			}
			while (true);
			norms.Insert(numDocNorms, norm);
			modifiedNorms.Insert(numDocNorms, norm);
			// System.out.println("creating norm("+numDocNorms+"): "+norm);
			numDocNorms++;
			lastNorm = (norm > 10?0:norm); // there's a limit to how many distinct
			// values can be stored in a ingle byte
			return norm;
		}
	}
}