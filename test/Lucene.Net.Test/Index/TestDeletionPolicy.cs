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
using System.Collections.Generic;
using NUnit.Framework;

using WhitespaceAnalyzer = Lucene.Net.Analysis.WhitespaceAnalyzer;
using Document = Lucene.Net.Documents.Document;
using Field = Lucene.Net.Documents.Field;
using Directory = Lucene.Net.Store.Directory;
using MockRAMDirectory = Lucene.Net.Store.MockRAMDirectory;
using RAMDirectory = Lucene.Net.Store.RAMDirectory;
using IndexSearcher = Lucene.Net.Search.IndexSearcher;
using Query = Lucene.Net.Search.Query;
using ScoreDoc = Lucene.Net.Search.ScoreDoc;
using TermQuery = Lucene.Net.Search.TermQuery;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

namespace Lucene.Net.Index
{
	
	/*
	Verify we can read the pre-2.1 file format, do searches
	against it, and add documents to it.*/
	
	[TestFixture]
	public class TestDeletionPolicy : LuceneTestCase
	{
		private void  VerifyCommitOrder<T>(IList<T> commits) where T : IndexCommit
		{
			IndexCommit firstCommit = commits[0];
			long last = SegmentInfos.GenerationFromSegmentsFileName(firstCommit.SegmentsFileName);
			Assert.AreEqual(last, firstCommit.Generation);
			long lastVersion = firstCommit.Version;
			long lastTimestamp = firstCommit.Timestamp(null);
			for (int i = 1; i < commits.Count; i++)
			{
				IndexCommit commit = commits[i];
				long now = SegmentInfos.GenerationFromSegmentsFileName(commit.SegmentsFileName);
				long nowVersion = commit.Version;
				long nowTimestamp = commit.Timestamp(null);
				Assert.IsTrue(now > last, "SegmentInfos commits are out-of-order");
				Assert.IsTrue(nowVersion > lastVersion, "SegmentInfos versions are out-of-order");
				Assert.IsTrue(nowTimestamp >= lastTimestamp, "SegmentInfos timestamps are out-of-order: now=" + nowTimestamp + " vs last=" + lastTimestamp);
				Assert.AreEqual(now, commit.Generation);
				last = now;
				lastVersion = nowVersion;
				lastTimestamp = nowTimestamp;
			}
		}
		
		internal class KeepAllDeletionPolicy : IndexDeletionPolicy
		{
			public KeepAllDeletionPolicy(TestDeletionPolicy enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestDeletionPolicy enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestDeletionPolicy enclosingInstance;
			public TestDeletionPolicy Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			internal int numOnInit;
			internal int numOnCommit;
			internal Directory dir;
			public virtual void  OnInit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				numOnInit++;
			}
			public virtual void  OnCommit<T>(IList<T> commits) where T : IndexCommit
			{
				IndexCommit lastCommit = (IndexCommit) commits[commits.Count - 1];
				IndexReader r = IndexReader.Open(dir, true, null);
				Assert.AreEqual(r.IsOptimized(), lastCommit.IsOptimized, "lastCommit.isOptimized()=" + lastCommit.IsOptimized + " vs IndexReader.isOptimized=" + r.IsOptimized());
				r.Close();
				Enclosing_Instance.VerifyCommitOrder(commits);
				numOnCommit++;
			}
		}
		
		/// <summary> This is useful for adding to a big index when you know
		/// readers are not using it.
		/// </summary>
		internal class KeepNoneOnInitDeletionPolicy : IndexDeletionPolicy
		{
			public KeepNoneOnInitDeletionPolicy(TestDeletionPolicy enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestDeletionPolicy enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestDeletionPolicy enclosingInstance;
			public TestDeletionPolicy Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			internal int numOnInit;
			internal int numOnCommit;
			public virtual void  OnInit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				numOnInit++;
				// On init, delete all commit points:
				System.Collections.IEnumerator it = commits.GetEnumerator();
				while (it.MoveNext())
				{
					IndexCommit commit = (IndexCommit) it.Current;
					commit.Delete();
					Assert.IsTrue(commit.IsDeleted);
				}
			}
			public virtual void  OnCommit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				int size = commits.Count;
				// Delete all but last one:
				for (int i = 0; i < size - 1; i++)
				{
					((IndexCommit) commits[i]).Delete();
				}
				numOnCommit++;
			}
		}
		
		internal class KeepLastNDeletionPolicy : IndexDeletionPolicy
		{
			private void  InitBlock(TestDeletionPolicy enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestDeletionPolicy enclosingInstance;
			public TestDeletionPolicy Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			internal int numOnInit;
			internal int numOnCommit;
			internal int numToKeep;
			internal int numDelete;
			internal System.Collections.Hashtable seen = new System.Collections.Hashtable();
			
			public KeepLastNDeletionPolicy(TestDeletionPolicy enclosingInstance, int numToKeep)
			{
				InitBlock(enclosingInstance);
				this.numToKeep = numToKeep;
			}
			
			public virtual void  OnInit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				numOnInit++;
				// do no deletions on init
				DoDeletes(commits, false);
			}
			
			public virtual void  OnCommit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				DoDeletes(commits, true);
			}
			
			private void  DoDeletes<T>(IList<T> commits, bool isCommit) where T : IndexCommit
			{
				
				// Assert that we really are only called for each new
				// commit:
				if (isCommit)
				{
					System.String fileName = commits[commits.Count - 1].SegmentsFileName;
					if (seen.Contains(fileName))
					{
						throw new System.SystemException("onCommit was called twice on the same commit point: " + fileName);
					}
					seen.Add(fileName, fileName);
					numOnCommit++;
				}
				int size = commits.Count;
				for (int i = 0; i < size - numToKeep; i++)
				{
					((IndexCommit) commits[i]).Delete();
					numDelete++;
				}
			}
		}
		
		/*
		* Delete a commit only when it has been obsoleted by N
		* seconds.
		*/
		internal class ExpirationTimeDeletionPolicy : IndexDeletionPolicy
		{
			private void  InitBlock(TestDeletionPolicy enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestDeletionPolicy enclosingInstance;
			public TestDeletionPolicy Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			
			internal Directory dir;
			internal double expirationTimeSeconds;
			internal int numDelete;
			
			public ExpirationTimeDeletionPolicy(TestDeletionPolicy enclosingInstance, Directory dir, double seconds)
			{
				InitBlock(enclosingInstance);
				this.dir = dir;
				this.expirationTimeSeconds = seconds;
			}
			
			public virtual void  OnInit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				OnCommit(commits);
			}
			
			public virtual void  OnCommit<T>(IList<T> commits) where T : IndexCommit
			{
				Enclosing_Instance.VerifyCommitOrder(commits);
				
				IndexCommit lastCommit = commits[commits.Count - 1];
				
				// Any commit older than expireTime should be deleted:
				double expireTime = dir.FileModified(lastCommit.SegmentsFileName, null) / 1000.0 - expirationTimeSeconds;
				
				System.Collections.IEnumerator it = commits.GetEnumerator();
				
				while (it.MoveNext())
				{
					IndexCommit commit = (IndexCommit) it.Current;
					double modTime = dir.FileModified(commit.SegmentsFileName, null) / 1000.0;
					if (commit != lastCommit && modTime < expireTime)
					{
						commit.Delete();
						numDelete += 1;
					}
				}
			}
		}
		
		/*
		* Test "by time expiration" deletion policy:
		*/
		[Test]
		public virtual void  TestExpirationTimeDeletionPolicy()
		{
			double SECONDS = 2.0;
			
			bool useCompoundFile = true;
			
			Directory dir = new RAMDirectory();
			ExpirationTimeDeletionPolicy policy = new ExpirationTimeDeletionPolicy(this, dir, SECONDS);
            IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
			writer.UseCompoundFile = useCompoundFile;
			writer.Close();
			
			long lastDeleteTime = 0;
			for (int i = 0; i < 7; i++)
			{
				// Record last time when writer performed deletes of
				// past commits
				lastDeleteTime = (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
                writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.UseCompoundFile = useCompoundFile;
				for (int j = 0; j < 17; j++)
				{
					AddDoc(writer);
				}
				writer.Close();
				
				// Make sure to sleep long enough so that some commit
				// points will be deleted:
				System.Threading.Thread.Sleep(new System.TimeSpan((System.Int64) 10000 * (int) (1000.0 * (SECONDS / 5.0))));
			}
			
			// First, make sure the policy in fact deleted something:
			Assert.IsTrue(policy.numDelete > 0, "no commits were deleted");
			
			// Then simplistic check: just verify that the
			// segments_N's that still exist are in fact within SECONDS
			// seconds of the last one's mod time, and, that I can
			// open a reader on each:
			long gen = SegmentInfos.GetCurrentSegmentGeneration(dir, null);
			
			System.String fileName = IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen);
			dir.DeleteFile(IndexFileNames.SEGMENTS_GEN, null);
			while (gen > 0)
			{
				try
				{
					IndexReader reader = IndexReader.Open(dir, true, null);
					reader.Close();
					fileName = IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen);
					long modTime = dir.FileModified(fileName, null);
					Assert.IsTrue(lastDeleteTime - modTime <= (SECONDS * 1000), "commit point was older than " + SECONDS + " seconds (" + (lastDeleteTime - modTime) + " msec) but did not get deleted");
				}
				catch (System.IO.IOException)
				{
					// OK
					break;
				}
				
				dir.DeleteFile(IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen), null);
				gen--;
			}
			
			dir.Close();
		}
		
		/*
		* Test a silly deletion policy that keeps all commits around.
		*/
		[Test]
		public virtual void  TestKeepAllDeletionPolicy()
		{
			
			for (int pass = 0; pass < 2; pass++)
			{
				bool useCompoundFile = (pass % 2) != 0;
				
				// Never deletes a commit
				KeepAllDeletionPolicy policy = new KeepAllDeletionPolicy(this);
				
				Directory dir = new RAMDirectory();
				policy.dir = dir;

                IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.SetMaxBufferedDocs(10);
				writer.UseCompoundFile = useCompoundFile;
				writer.SetMergeScheduler(new SerialMergeScheduler(), null);
				for (int i = 0; i < 107; i++)
				{
					AddDoc(writer);
				}
				writer.Close();

                writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.UseCompoundFile = useCompoundFile;
				writer.Optimize(null);
				writer.Close();
				
				Assert.AreEqual(2, policy.numOnInit);

				// If we are not auto committing then there should
				// be exactly 2 commits (one per close above):
				Assert.AreEqual(2, policy.numOnCommit);
				
				// Test listCommits
				ICollection<IndexCommit> commits = IndexReader.ListCommits(dir, null);
				// 1 from opening writer + 2 from closing writer
				Assert.AreEqual(3, commits.Count);
				
				System.Collections.IEnumerator it = commits.GetEnumerator();
				// Make sure we can open a reader on each commit:
				while (it.MoveNext())
				{
					IndexCommit commit = (IndexCommit) it.Current;
					IndexReader r = IndexReader.Open(commit, null, false, null);
					r.Close();
				}
				
				// Simplistic check: just verify all segments_N's still
				// exist, and, I can open a reader on each:
				dir.DeleteFile(IndexFileNames.SEGMENTS_GEN, null);
				long gen = SegmentInfos.GetCurrentSegmentGeneration(dir, null);
				while (gen > 0)
				{
					IndexReader reader = IndexReader.Open(dir, true, null);
					reader.Close();
					dir.DeleteFile(IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen), null);
					gen--;
					
					if (gen > 0)
					{
						// Now that we've removed a commit point, which
						// should have orphan'd at least one index file.
						// Open & close a writer and assert that it
						// actually removed something:
						int preCount = dir.ListAll(null).Length;
						writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.LIMITED, null);
						writer.Close();
						int postCount = dir.ListAll(null).Length;
						Assert.IsTrue(postCount < preCount);
					}
				}
				
				dir.Close();
			}
		}
		
		/* Uses KeepAllDeletionPolicy to keep all commits around,
		* then, opens a new IndexWriter on a previous commit
		* point. */
		[Test]
		public virtual void  TestOpenPriorSnapshot()
		{
			
			// Never deletes a commit
			KeepAllDeletionPolicy policy = new KeepAllDeletionPolicy(this);
			
			Directory dir = new MockRAMDirectory();
			policy.dir = dir;
			
			IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), policy, IndexWriter.MaxFieldLength.LIMITED, null);
			writer.SetMaxBufferedDocs(2);
			for (int i = 0; i < 10; i++)
			{
				AddDoc(writer);
				if ((1 + i) % 2 == 0)
					writer.Commit(null);
			}
			writer.Close();
			
			ICollection<IndexCommit> commits = IndexReader.ListCommits(dir, null);
			Assert.AreEqual(6, commits.Count);
			IndexCommit lastCommit = null;
			System.Collections.IEnumerator it = commits.GetEnumerator();
			while (it.MoveNext())
			{
				IndexCommit commit = (IndexCommit) it.Current;
				if (lastCommit == null || commit.Generation > lastCommit.Generation)
					lastCommit = commit;
			}
			Assert.IsTrue(lastCommit != null);
			
			// Now add 1 doc and optimize
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), policy, IndexWriter.MaxFieldLength.LIMITED, null);
			AddDoc(writer);
			Assert.AreEqual(11, writer.NumDocs(null));
			writer.Optimize(null);
			writer.Close();
			
			Assert.AreEqual(7, IndexReader.ListCommits(dir, null).Count);
			
			// Now open writer on the commit just before optimize:
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), policy, IndexWriter.MaxFieldLength.LIMITED, lastCommit, null);
			Assert.AreEqual(10, writer.NumDocs(null));
			
			// Should undo our rollback:
			writer.Rollback(null);
			
			IndexReader r = IndexReader.Open(dir, true, null);
			// Still optimized, still 11 docs
			Assert.IsTrue(r.IsOptimized());
			Assert.AreEqual(11, r.NumDocs());
			r.Close();
			
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), policy, IndexWriter.MaxFieldLength.LIMITED, lastCommit, null);
			Assert.AreEqual(10, writer.NumDocs(null));
			// Commits the rollback:
			writer.Close();
			
			// Now 8 because we made another commit
			Assert.AreEqual(8, IndexReader.ListCommits(dir, null).Count);
			
			r = IndexReader.Open(dir, true, null);
			// Not optimized because we rolled it back, and now only
			// 10 docs
			Assert.IsTrue(!r.IsOptimized());
			Assert.AreEqual(10, r.NumDocs());
			r.Close();
			
			// Reoptimize
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), policy, IndexWriter.MaxFieldLength.LIMITED, null);
			writer.Optimize(null);
			writer.Close();
			
			r = IndexReader.Open(dir, true, null);
			Assert.IsTrue(r.IsOptimized());
			Assert.AreEqual(10, r.NumDocs());
			r.Close();
			
			// Now open writer on the commit just before optimize,
			// but this time keeping only the last commit:
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), new KeepOnlyLastCommitDeletionPolicy(), IndexWriter.MaxFieldLength.LIMITED, lastCommit, null);
			Assert.AreEqual(10, writer.NumDocs(null));
			
			// Reader still sees optimized index, because writer
			// opened on the prior commit has not yet committed:
			r = IndexReader.Open(dir, true, null);
			Assert.IsTrue(r.IsOptimized());
			Assert.AreEqual(10, r.NumDocs());
			r.Close();
			
			writer.Close();
			
			// Now reader sees unoptimized index:
			r = IndexReader.Open(dir, true, null);
			Assert.IsTrue(!r.IsOptimized());
			Assert.AreEqual(10, r.NumDocs());
			r.Close();
			
			dir.Close();
		}
		
		
		/* Test keeping NO commit points.  This is a viable and
		* useful case eg where you want to build a big index and
		* you know there are no readers.
		*/
		[Test]
		public virtual void  TestKeepNoneOnInitDeletionPolicy()
		{
			for (int pass = 0; pass < 2; pass++)
			{
				bool useCompoundFile = (pass % 2) != 0;
				
				KeepNoneOnInitDeletionPolicy policy = new KeepNoneOnInitDeletionPolicy(this);
				
				Directory dir = new RAMDirectory();

                IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.SetMaxBufferedDocs(10);
				writer.UseCompoundFile = useCompoundFile;
				for (int i = 0; i < 107; i++)
				{
					AddDoc(writer);
				}
				writer.Close();

                writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.UseCompoundFile = useCompoundFile;
				writer.Optimize(null);
				writer.Close();
				
				Assert.AreEqual(2, policy.numOnInit);
				// If we are not auto committing then there should
				// be exactly 2 commits (one per close above):
				Assert.AreEqual(2, policy.numOnCommit);
				
				// Simplistic check: just verify the index is in fact
				// readable:
				IndexReader reader = IndexReader.Open(dir, true, null);
				reader.Close();
				
				dir.Close();
			}
		}
		
		/*
		* Test a deletion policy that keeps last N commits.
		*/
		[Test]
		public virtual void  TestKeepLastNDeletionPolicy()
		{
			int N = 5;
			
			for (int pass = 0; pass < 2; pass++)
			{
				bool useCompoundFile = (pass % 2) != 0;
				
				Directory dir = new RAMDirectory();
				
				KeepLastNDeletionPolicy policy = new KeepLastNDeletionPolicy(this, N);
				
				for (int j = 0; j < N + 1; j++)
				{
                    IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
					writer.SetMaxBufferedDocs(10);
					writer.UseCompoundFile = useCompoundFile;
					for (int i = 0; i < 17; i++)
					{
						AddDoc(writer);
					}
					writer.Optimize(null);
					writer.Close();
				}
				
				Assert.IsTrue(policy.numDelete > 0);
				Assert.AreEqual(N + 1, policy.numOnInit);
				Assert.AreEqual(N + 1, policy.numOnCommit);
				
				// Simplistic check: just verify only the past N segments_N's still
				// exist, and, I can open a reader on each:
				dir.DeleteFile(IndexFileNames.SEGMENTS_GEN, null);
				long gen = SegmentInfos.GetCurrentSegmentGeneration(dir, null);
				for (int i = 0; i < N + 1; i++)
				{
					try
					{
						IndexReader reader = IndexReader.Open(dir, true, null);
						reader.Close();
						if (i == N)
						{
							Assert.Fail("should have failed on commits prior to last " + N);
						}
					}
					catch (System.IO.IOException e)
					{
						if (i != N)
						{
							throw e;
						}
					}
					if (i < N)
					{
						dir.DeleteFile(IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen), null);
					}
					gen--;
				}
				
				dir.Close();
			}
		}
		
		/*
		* Test a deletion policy that keeps last N commits
		* around, with reader doing deletes.
		*/
		[Test]
		public virtual void  TestKeepLastNDeletionPolicyWithReader()
		{
			int N = 10;
			
			for (int pass = 0; pass < 2; pass++)
			{
				bool useCompoundFile = (pass % 2) != 0;
				
				KeepLastNDeletionPolicy policy = new KeepLastNDeletionPolicy(this, N);
				
				Directory dir = new RAMDirectory();
                IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.UseCompoundFile = useCompoundFile;
				writer.Close();
				Term searchTerm = new Term("content", "aaa");
				Query query = new TermQuery(searchTerm);
				
				for (int i = 0; i < N + 1; i++)
				{
                    writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
					writer.UseCompoundFile = useCompoundFile;
					for (int j = 0; j < 17; j++)
					{
						AddDoc(writer);
					}
					// this is a commit
					writer.Close();
					IndexReader reader = IndexReader.Open(dir, policy, false, null);
					reader.DeleteDocument(3 * i + 1, null);
					reader.SetNorm(4 * i + 1, "content", 2.0F, null);
					IndexSearcher searcher = new IndexSearcher(reader);
					ScoreDoc[] hits = searcher.Search(query, null, 1000, null).ScoreDocs;
					Assert.AreEqual(16 * (1 + i), hits.Length);
					// this is a commit
					reader.Close();
					searcher.Close();
				}
                writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.UseCompoundFile = useCompoundFile;
				writer.Optimize(null);
				// this is a commit
				writer.Close();
				
				Assert.AreEqual(2 * (N + 2), policy.numOnInit);
				Assert.AreEqual(2 * (N + 2) - 1, policy.numOnCommit);
				
				IndexSearcher searcher2 = new IndexSearcher(dir, false, null);
				ScoreDoc[] hits2 = searcher2.Search(query, null, 1000, null).ScoreDocs;
				Assert.AreEqual(176, hits2.Length);
				
				// Simplistic check: just verify only the past N segments_N's still
				// exist, and, I can open a reader on each:
				long gen = SegmentInfos.GetCurrentSegmentGeneration(dir, null);
				
				dir.DeleteFile(IndexFileNames.SEGMENTS_GEN, null);
				int expectedCount = 176;
				
				for (int i = 0; i < N + 1; i++)
				{
					try
					{
						IndexReader reader = IndexReader.Open(dir, true, null);
						
						// Work backwards in commits on what the expected
						// count should be.
						searcher2 = new IndexSearcher(reader);
						hits2 = searcher2.Search(query, null, 1000, null).ScoreDocs;
						if (i > 1)
						{
							if (i % 2 == 0)
							{
								expectedCount += 1;
							}
							else
							{
								expectedCount -= 17;
							}
						}
						Assert.AreEqual(expectedCount, hits2.Length);
						searcher2.Close();
						reader.Close();
						if (i == N)
						{
							Assert.Fail("should have failed on commits before last 5");
						}
					}
					catch (System.IO.IOException e)
					{
						if (i != N)
						{
							throw e;
						}
					}
					if (i < N)
					{
						dir.DeleteFile(IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen), null);
					}
					gen--;
				}
				
				dir.Close();
			}
		}
		
		/*
		* Test a deletion policy that keeps last N commits
		* around, through creates.
		*/
		[Test]
		public virtual void  TestKeepLastNDeletionPolicyWithCreates()
		{
			int N = 10;
			
			for (int pass = 0; pass < 2; pass++)
			{
				bool useCompoundFile = (pass % 2) != 0;
				
				KeepLastNDeletionPolicy policy = new KeepLastNDeletionPolicy(this, N);
				
				Directory dir = new RAMDirectory();
                IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
				writer.SetMaxBufferedDocs(10);
				writer.UseCompoundFile = useCompoundFile;
				writer.Close();
				Term searchTerm = new Term("content", "aaa");
				Query query = new TermQuery(searchTerm);
				
				for (int i = 0; i < N + 1; i++)
				{

                    writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
					writer.SetMaxBufferedDocs(10);
					writer.UseCompoundFile = useCompoundFile;
					for (int j = 0; j < 17; j++)
					{
						AddDoc(writer);
					}
					// this is a commit
					writer.Close();
					IndexReader reader = IndexReader.Open(dir, policy, false, null);
					reader.DeleteDocument(3, null);
					reader.SetNorm(5, "content", 2.0F, null);
					IndexSearcher searcher = new IndexSearcher(reader);
					ScoreDoc[] hits = searcher.Search(query, null, 1000, null).ScoreDocs;
					Assert.AreEqual(16, hits.Length);
					// this is a commit
					reader.Close();
					searcher.Close();

                    writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, policy, IndexWriter.MaxFieldLength.UNLIMITED, null);
					// This will not commit: there are no changes
					// pending because we opened for "create":
					writer.Close();
				}
				
				Assert.AreEqual(1 + 3 * (N + 1), policy.numOnInit);
				Assert.AreEqual(3 * (N + 1), policy.numOnCommit);
				
				IndexSearcher searcher2 = new IndexSearcher(dir, false, null);
				ScoreDoc[] hits2 = searcher2.Search(query, null, 1000, null).ScoreDocs;
				Assert.AreEqual(0, hits2.Length);
				
				// Simplistic check: just verify only the past N segments_N's still
				// exist, and, I can open a reader on each:
				long gen = SegmentInfos.GetCurrentSegmentGeneration(dir, null);
				
				dir.DeleteFile(IndexFileNames.SEGMENTS_GEN, null);
				int expectedCount = 0;
				
				for (int i = 0; i < N + 1; i++)
				{
					try
					{
						IndexReader reader = IndexReader.Open(dir, true, null);
						
						// Work backwards in commits on what the expected
						// count should be.
						searcher2 = new IndexSearcher(reader);
						hits2 = searcher2.Search(query, null, 1000, null).ScoreDocs;
						Assert.AreEqual(expectedCount, hits2.Length);
						searcher2.Close();
						if (expectedCount == 0)
						{
							expectedCount = 16;
						}
						else if (expectedCount == 16)
						{
							expectedCount = 17;
						}
						else if (expectedCount == 17)
						{
							expectedCount = 0;
						}
						reader.Close();
						if (i == N)
						{
							Assert.Fail("should have failed on commits before last " + N);
						}
					}
					catch (System.IO.IOException e)
					{
						if (i != N)
						{
							throw e;
						}
					}
					if (i < N)
					{
						dir.DeleteFile(IndexFileNames.FileNameFromGeneration(IndexFileNames.SEGMENTS, "", gen), null);
					}
					gen--;
				}
				
				dir.Close();
			}
		}
		
		private void  AddDoc(IndexWriter writer)
		{
			Document doc = new Document();
			doc.Add(new Field("content", "aaa", Field.Store.NO, Field.Index.ANALYZED));
			writer.AddDocument(doc, null);
		}
	}
}