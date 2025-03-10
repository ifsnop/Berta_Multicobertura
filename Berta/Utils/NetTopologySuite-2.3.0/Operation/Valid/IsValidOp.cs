using System;
using System.Collections;
using System.Collections.Generic;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.GeometriesGraph;
using NetTopologySuite.Utilities;

namespace NetTopologySuite.Operation.Valid
{
    /// <summary>
    /// Implements the algorithms required to compute the <see cref="Geometry.IsValid" />
    /// method for <see cref="Geometry" />s.
    /// See the documentation for the various geometry types for a specification of validity.
    /// </summary>
    public class IsValidOp
    {
        /// <summary>
        /// Checks whether a coordinate is valid for processing.
        /// Coordinates are valid iff their x and y ordinates are in the
        /// range of the floating point representation.
        /// </summary>
        /// <param name="coord">The coordinate to validate.</param>
        /// <returns><c>true</c> if the coordinate is valid.</returns>
        public static bool IsValidCoordinate(Coordinate coord)
        {
            if (double.IsNaN(coord.X))
                return false;
            if (double.IsInfinity(coord.X))
                return false;
            if (double.IsNaN(coord.Y))
                return false;
            if (double.IsInfinity(coord.Y))
                return false;
            return true;
        }

        /// <summary>
        /// Find a point from the list of testCoords
        /// that is NOT a node in the edge for the list of searchCoords.
        /// </summary>
        /// <param name="testCoords"></param>
        /// <param name="searchRing"></param>
        /// <param name="graph"></param>
        /// <returns>The point found, or <c>null</c> if none found.</returns>
        public static Coordinate FindPointNotNode(Coordinate[] testCoords, LinearRing searchRing, GeometryGraph graph)
        {
            // find edge corresponding to searchRing.
            var searchEdge = graph.FindEdge(searchRing);
            // find a point in the testCoords which is not a node of the searchRing
            var eiList = searchEdge.EdgeIntersectionList;
            // somewhat inefficient - is there a better way? (Use a node map, for instance?)
            foreach(var pt in testCoords)
                if(!eiList.IsIntersection(pt))
                    return pt;
            return null;
        }

        private readonly Geometry _parentGeometry;  // the base Geometry to be validated

        /*
         * If the following condition is TRUE JTS will validate inverted shells and exverted holes (the ESRI SDE model).
         */
        private bool _isSelfTouchingRingFormingHoleValid;
        private TopologyValidationError _validErr;

        /// <summary>
        ///
        /// </summary>
        /// <param name="parentGeometry"></param>
        public IsValidOp(Geometry parentGeometry)
        {
            _parentGeometry = parentGeometry;
        }

        /// <summary>
        /// <para>
        /// Gets/Sets whether polygons using Self-Touching Rings to form
        /// holes are reported as valid.
        /// If this flag is set, the following Self-Touching conditions
        /// are treated as being valid:
        /// - The shell ring self-touches to create a hole touching the shell.
        /// - A hole ring self-touches to create two holes touching at a point.
        /// </para>
        /// <para>
        /// The default (following the OGC SFS standard)
        /// is that this condition is not valid (<c>false</c>).
        /// </para>
        /// <para>
        /// This does not affect whether Self-Touching Rings
        /// disconnecting the polygon interior are considered valid
        /// (these are considered to be invalid under the SFS, and many other
        /// spatial models as well).
        /// This includes "bow-tie" shells,
        /// which self-touch at a single point causing the interior to be disconnected,
        /// and "C-shaped" holes which self-touch at a single point causing an island to be formed.
        /// </para>
        /// </summary>
        /// <value>States whether geometry with this condition is valid.</value>
        public bool IsSelfTouchingRingFormingHoleValid
        {
            get => _isSelfTouchingRingFormingHoleValid;
            set => _isSelfTouchingRingFormingHoleValid = value;
        }

        /// <summary>
        /// Computes the validity of the geometry,
        /// and returns <tt>true</tt> if it is valid.
        /// </summary>
        public bool IsValid
        {
            get
            {
                CheckValid(_parentGeometry);
                return _validErr == null;
            }
        }

        /// <summary>
        /// Computes the validity of the geometry,
        /// and if not valid returns the validation error for the geometry,
        /// or null if the geometry is valid.
        /// </summary>
        /// <returns>The validation error, if the geometry is invalid <br/>
        /// or <c>null</c> if the geometry is valid</returns>
        public TopologyValidationError ValidationError
        {
            get
            {
                CheckValid(_parentGeometry);
                return _validErr;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(Geometry g)
        {
            _validErr = null;

            if (g.IsEmpty) return;

            if (g is Point pt)
                CheckValid(pt);
            else if (g is MultiPoint mpt)
                CheckValid(mpt);
            else if (g is LinearRing) // LineString also handles LinearRings
                CheckValid((LinearRing) g);
            else if (g is LineString)
                CheckValid((LineString) g);
            else if (g is Polygon)
                CheckValid((Polygon) g);
            else if (g is MultiPolygon)
                CheckValid((MultiPolygon) g);
            else if (g is GeometryCollection)
                CheckValid((GeometryCollection) g);
            else throw new NotSupportedException(g.GetType().FullName);
        }

        /// <summary>
        /// Checks validity of a Point.
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(Point g)
        {
            CheckInvalidCoordinates(g.CoordinateSequence);
        }

        /// <summary>
        /// Checks validity of a MultiPoint.
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(MultiPoint g)
        {
            CheckInvalidCoordinates(g);
        }

        /// <summary>
        /// Checks validity of a LineString.
        /// Almost anything goes for lineStrings!
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(LineString g)
        {
            CheckInvalidCoordinates(g.CoordinateSequence);
            if (_validErr != null) return;
            var graph = new GeometryGraph(0, g);
            CheckTooFewPoints(graph);
        }

        /// <summary>
        /// Checks validity of a LinearRing.
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(LinearRing g)
        {
            CheckInvalidCoordinates(g.CoordinateSequence);
            if (_validErr != null) return;
            CheckClosedRing(g);
            if (_validErr != null) return;

            var graph = new GeometryGraph(0, g);
            CheckTooFewPoints(graph);
            if (_validErr != null) return;
            var li = new RobustLineIntersector();
            graph.ComputeSelfNodes(li, true, true);
            CheckNoSelfIntersectingRings(graph);
        }

        /// <summary>
        /// Checks the validity of a polygon and sets the validErr flag.
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(Polygon g)
        {
            CheckInvalidCoordinates(g);
            if (_validErr != null) return;
            CheckClosedRings(g);
            if (_validErr != null) return;

            var graph = new GeometryGraph(0, g);
            CheckTooFewPoints(graph);
            if (_validErr != null) return;
            CheckConsistentArea(graph);
            if (_validErr != null) return;
            if (!IsSelfTouchingRingFormingHoleValid)
            {
                CheckNoSelfIntersectingRings(graph);
                if (_validErr != null) return;
            }
            CheckHolesInShell(g, graph);
            if (_validErr != null) return;
            CheckHolesNotNested(g, graph);
            if (_validErr != null) return;
            CheckConnectedInteriors(graph);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="g"></param>
        private void CheckValid(MultiPolygon g)
        {
            foreach(Polygon p in g.Geometries)
            {
                CheckInvalidCoordinates(p);
                if (_validErr != null) return;
                CheckClosedRings(p);
                if (_validErr != null) return;
            }

            var graph = new GeometryGraph(0, g);
            CheckTooFewPoints(graph);
            if (_validErr != null) return;
            CheckConsistentArea(graph);
            if (_validErr != null) return;
            if (!IsSelfTouchingRingFormingHoleValid)
            {
                CheckNoSelfIntersectingRings(graph);
                if (_validErr != null) return;
            }
            foreach(Polygon p in g.Geometries)
            {
                CheckHolesInShell(p, graph);
                if (_validErr != null) return;
            }
            foreach (Polygon p in g.Geometries)
            {
                CheckHolesNotNested(p, graph);
                if (_validErr != null) return;
            }
            CheckShellsNotNested(g, graph);
            if (_validErr != null) return;
            CheckConnectedInteriors(graph);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="gc"></param>
        private void CheckValid(GeometryCollection gc)
        {
            foreach(var g in gc.Geometries)
            {
                CheckValid(g);
                if (_validErr != null) return;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="coords"></param>
        private void CheckInvalidCoordinates(CoordinateSequence coords)
        {
            var flt = new InvalidCoordinateFilter();
            flt.Filter(coords);
            if (flt.HasInvalidCoordinate)
                _validErr = new TopologyValidationError(TopologyValidationErrors.InvalidCoordinate, flt.InvalidCoordinate);
        }

        private void CheckInvalidCoordinates(Geometry geometry)
        {
            var flt = new InvalidCoordinateFilter();
            geometry.Apply(flt);

            if (flt.HasInvalidCoordinate)
                _validErr = new TopologyValidationError(TopologyValidationErrors.InvalidCoordinate, flt.InvalidCoordinate);
        }

        private void CheckInvalidCoordinates(Polygon poly)
        {
            CheckInvalidCoordinates(poly.ExteriorRing.CoordinateSequence);
            if (_validErr != null) return;
            foreach (var ls in poly.InteriorRings)
            {
                CheckInvalidCoordinates(ls.CoordinateSequence);
                if (_validErr != null) return;
            }
        }

        private void CheckClosedRings(Polygon poly)
        {
            CheckClosedRing(poly.Shell);
            if (_validErr != null) return;
            foreach (var hole in poly.Holes)
            {
                CheckClosedRing(hole);
                if (_validErr != null) return;
            }
        }

        private void CheckClosedRing(LinearRing ring)
        {
            if (ring.IsEmpty)
            {
                return;
            }

            if (!ring.IsClosed)
                _validErr = new TopologyValidationError(TopologyValidationErrors.RingNotClosed,
                    ring.GetCoordinateN(0));
        }

        private void CheckTooFewPoints(GeometryGraph graph)
        {
            if (graph.HasTooFewPoints)
            {
                _validErr = new TopologyValidationError(TopologyValidationErrors.TooFewPoints,
                    graph.InvalidPoint);
                return;
            }
        }

        private void CheckConsistentArea(GeometryGraph graph)
        {
            var cat = new ConsistentAreaTester(graph);
            bool isValidArea = cat.IsNodeConsistentArea;
            if (!isValidArea)
            {
                _validErr = new TopologyValidationError(TopologyValidationErrors.SelfIntersection, cat.InvalidPoint);
                return;
            }
            if (cat.HasDuplicateRings)
            {
                _validErr = new TopologyValidationError(TopologyValidationErrors.DuplicateRings, cat.InvalidPoint);
                return;
            }
        }

        /// <summary>
        /// Check that there is no ring which self-intersects (except of course at its endpoints).
        /// This is required by OGC topology rules (but not by other models
        /// such as ESRI SDE, which allow inverted shells and exverted holes).
        /// </summary>
        private void CheckNoSelfIntersectingRings(GeometryGraph graph)
        {
            for (IEnumerator i = graph.GetEdgeEnumerator(); i.MoveNext(); )
            {
                var e = (Edge) i.Current;
                CheckNoSelfIntersectingRing(e.EdgeIntersectionList);
                if (_validErr != null) return;
            }
        }

        /// <summary>
        /// Check that a ring does not self-intersect, except at its endpoints.
        /// Algorithm is to count the number of times each node along edge occurs.
        /// If any occur more than once, that must be a self-intersection.
        /// </summary>
        private void CheckNoSelfIntersectingRing(EdgeIntersectionList eiList)
        {
            var nodeSet = new HashSet<Coordinate>();
            bool isFirst = true;
            foreach(var ei in eiList)
            {
                if (isFirst)
                {
                    isFirst = false;
                    continue;
                }
                if (nodeSet.Contains(ei.Coordinate))
                {
                    _validErr = new TopologyValidationError(TopologyValidationErrors.RingSelfIntersection, ei.Coordinate);
                    return;
                }
                else nodeSet.Add(ei.Coordinate);
            }
        }

        /// <summary>
        /// Tests that each hole is inside the polygon shell.
        /// This routine assumes that the holes have previously been tested
        /// to ensure that all vertices lie on the shell or inside it.
        /// A simple test of a single point in the hole can be used,
        /// provide the point is chosen such that it does not lie on the
        /// boundary of the shell.
        /// </summary>
        /// <param name="p">The polygon to be tested for hole inclusion.</param>
        /// <param name="graph">A GeometryGraph incorporating the polygon.</param>
        private void CheckHolesInShell(Polygon p, GeometryGraph graph)
        {
            // skip test if no holes are present
            if (p.NumInteriorRings <= 0) return;

            var shell = p.Shell;
            bool isShellEmpty = shell.IsEmpty;

            var pir = new IndexedPointInAreaLocator(shell);

            for (int i = 0; i < p.NumInteriorRings; i++)
            {
                var hole = p.Holes[i];
                if (hole.IsEmpty)
                {
                    continue;
                }

                var holePt = FindPointNotNode(hole.Coordinates, shell, graph);

                /*
                 * If no non-node hole vertex can be found, the hole must
                 * split the polygon into disconnected interiors.
                 * This will be caught by a subsequent check.
                 */
                if (holePt == null)
                    return;

                bool outside = isShellEmpty || Location.Exterior == pir.Locate(holePt);
                if(outside)
                {
                    _validErr = new TopologyValidationError(TopologyValidationErrors.HoleOutsideShell, holePt);
                    return;
                }
            }
        }

        /// <summary>
        /// Tests that no hole is nested inside another hole.
        /// This routine assumes that the holes are disjoint.
        /// To ensure this, holes have previously been tested
        /// to ensure that:
        /// They do not partially overlap
        /// (checked by <c>checkRelateConsistency</c>).
        /// They are not identical
        /// (checked by <c>checkRelateConsistency</c>).
        /// </summary>
        private void CheckHolesNotNested(Polygon p, GeometryGraph graph)
        {
            // skip test if no holes are present
            if (p.NumInteriorRings <= 0) return;

            var nestedTester = new IndexedNestedRingTester(graph);
            foreach (var innerHole in p.Holes)
            {
                if (innerHole.IsEmpty)
                {
                    continue;
                }

                nestedTester.Add(innerHole);
            }

            bool isNonNested = nestedTester.IsNonNested();
            if (!isNonNested)
                _validErr = new TopologyValidationError(TopologyValidationErrors.NestedHoles,
                    nestedTester.NestedPoint);
        }

        /// <summary>
        /// Tests that no element polygon is wholly in the interior of another element polygon.
        /// Preconditions:
        /// Shells do not partially overlap.
        /// Shells do not touch along an edge.
        /// No duplicate rings exists.
        /// This routine relies on the fact that while polygon shells may touch at one or
        /// more vertices, they cannot touch at ALL vertices.
        /// </summary>
        private void CheckShellsNotNested(MultiPolygon mp, GeometryGraph graph)
        {
            for (int i = 0; i < mp.NumGeometries; i++)
            {
                var p = (Polygon) mp.GetGeometryN(i);
                var shell = p.Shell;
                for (int j = 0; j < mp.NumGeometries; j++)
                {
                    if (i == j)
                        continue;
                    var p2 = (Polygon) mp.GetGeometryN(j);
                    CheckShellNotNested(shell, p2, graph);
                    if (_validErr != null) return;
                }
            }
        }

        /// <summary>
        /// Check if a shell is incorrectly nested within a polygon.  This is the case
        /// if the shell is inside the polygon shell, but not inside a polygon hole.
        /// (If the shell is inside a polygon hole, the nesting is valid.)
        /// The algorithm used relies on the fact that the rings must be properly contained.
        /// E.g. they cannot partially overlap (this has been previously checked by
        /// <c>CheckRelateConsistency</c>).
        /// </summary>
        private void CheckShellNotNested(LinearRing shell, Polygon p, GeometryGraph graph)
        {
            var shellPts = shell.Coordinates;
            // test if shell is inside polygon shell
            var polyShell = p.Shell;
            if (polyShell.IsEmpty)
            {
                return;
            }

            var polyPts = polyShell.Coordinates;
            var shellPt = FindPointNotNode(shellPts, polyShell, graph);
            // if no point could be found, we can assume that the shell is outside the polygon
            if (shellPt == null) return;
            bool insidePolyShell = PointLocation.IsInRing(shellPt, polyPts);
            if (!insidePolyShell) return;
            // if no holes, this is an error!
            if (p.NumInteriorRings <= 0)
            {
                _validErr = new TopologyValidationError(TopologyValidationErrors.NestedShells, shellPt);
                return;
            }

            /*
             * Check if the shell is inside one of the holes.
             * This is the case if one of the calls to checkShellInsideHole
             * returns a null coordinate.
             * Otherwise, the shell is not properly contained in a hole, which is an error.
             */
            Coordinate badNestedPt = null;
            for (int i = 0; i < p.NumInteriorRings; i++)
            {
                var hole = p.Holes[i];
                badNestedPt = CheckShellInsideHole(shell, hole, graph);
                if (badNestedPt == null) return;
            }
            _validErr = new TopologyValidationError(TopologyValidationErrors.NestedShells, badNestedPt);
        }

        /// <summary>
        /// This routine checks to see if a shell is properly contained in a hole.
        /// It assumes that the edges of the shell and hole do not
        /// properly intersect.
        /// </summary>
        /// <param name="shell"></param>
        /// <param name="hole"></param>
        /// <param name="graph"></param>
        /// <returns>
        /// <c>null</c> if the shell is properly contained, or
        /// a Coordinate which is not inside the hole if it is not.
        /// </returns>
        private Coordinate CheckShellInsideHole(LinearRing shell, LinearRing hole, GeometryGraph graph)
        {
            var shellPts = shell.Coordinates;
            var holePts = hole.Coordinates;
            // TODO: improve performance of this - by sorting pointlists?
            var shellPt = FindPointNotNode(shellPts, hole, graph);
            // if point is on shell but not hole, check that the shell is inside the hole
            if (shellPt != null)
            {
                bool insideHole = PointLocation.IsInRing(shellPt, holePts);
                if (!insideHole) return shellPt;
            }
            var holePt = FindPointNotNode(holePts, shell, graph);
            // if point is on hole but not shell, check that the hole is outside the shell
            if (holePt != null)
            {
                bool insideShell = PointLocation.IsInRing(holePt, shellPts);
                if (insideShell)
                    return holePt;
                return null;
            }
            Assert.ShouldNeverReachHere("points in shell and hole appear to be equal");
            return null;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="graph"></param>
        private void CheckConnectedInteriors(GeometryGraph graph)
        {
            var cit = new ConnectedInteriorTester(graph);
            if (!cit.IsInteriorsConnected())
                _validErr = new TopologyValidationError(TopologyValidationErrors.DisconnectedInteriors,
                    cit.Coordinate);
        }

        private class InvalidCoordinateFilter : IEntireCoordinateSequenceFilter
        {
            /// <summary>
            /// Gets or sets the sequence index that is being investigated
            /// </summary>
            public int SequenceIndex { get; set; } = -1;

            /// <summary>
            /// Gets or sets the offset of the coordinate
            /// </summary>
            private int Offset { get; set; }

            /// <inheritdoc cref="IEntireCoordinateSequenceFilter.Done"/>
            public bool Done { get; private set; }

            /// <inheritdoc cref="IEntireCoordinateSequenceFilter.GeometryChanged"/>
            /// <returns>Always <c>false</c></returns>
            public bool GeometryChanged { get => false; }

            /// <summary>
            /// Gets a value indicating the index of the coordinate that is invalid
            /// </summary>
            public int InvalidCoordinateIndex { get; private set; } = -1;

            /// <summary>
            /// Gets a value indicating the index of the coordinate that is invalid
            /// </summary>
            public int InvalidCoordinateIndexInSequence { get; private set; } = -1;
            /// <summary>
            /// Gets a value indicating the invalid coordinate value
            /// </summary>
            public Coordinate InvalidCoordinate { get; private set; }

            /// <summary>
            /// Gets a value indicating if the geometry that this filter was applied to
            /// has an invalid coordinate.
            /// </summary>
            public bool HasInvalidCoordinate
            {
                get => InvalidCoordinateIndex >= 0;
            }

            public void Filter(CoordinateSequence seq)
            {
                SequenceIndex++;
                for (int i = 0; i < seq.Count; i++)
                {
                    if (!CheckValidCoordinate(seq.GetX(i), seq.GetY(i)))
                    {
                        InvalidCoordinateIndex = Offset + i;
                        InvalidCoordinateIndexInSequence = i;
                        InvalidCoordinate = seq.GetCoordinateCopy(i);
                        Done = true;

                        return;
                    }
                }
                Offset += seq.Count;
            }

            private bool CheckValidCoordinate(double x, double y)
            {
                if (double.IsNaN(x))
                    return false;
                if (double.IsInfinity(x))
                    return false;
                if (double.IsNaN(y))
                    return false;
                if (double.IsInfinity(y))
                    return false;
                return true;
            }
        }
    }
}
