﻿//
// Revit IFC Import library: this library works with Autodesk(R) Revit(R) to import IFC files.
// Copyright (C) 2013  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;
using Revit.IFC.Import.Geometry;
using Revit.IFC.Import.Utility;

namespace Revit.IFC.Import.Data
{
    /// <summary>
    /// Represents the object placement.
    /// </summary>
    public class IFCLocation : IFCEntity
    {
        IFCLocation m_RelativeTo;

        Transform m_RelativeTransform = Transform.Identity;

        /// <summary>
        /// The total transform.
        /// </summary>
        public Transform TotalTransform
        {
            get { return m_RelativeTo != null ? m_RelativeTo.TotalTransform.Multiply(RelativeTransform) : RelativeTransform; }
        }

        /// <summary>
        /// The relative transform.
        /// </summary>
        public Transform RelativeTransform
        {
            get { return m_RelativeTransform; }
            protected set { m_RelativeTransform = value; }
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        protected IFCLocation()
        {

        }

        /// <summary>
        /// Constructs an IFCLocation from the IfcObjectPlacement handle.
        /// </summary>
        /// <param name="ifcObjectPlacement">The IfcObjectPlacement handle.</param>
        protected IFCLocation(IFCAnyHandle ifcObjectPlacement)
        {
            Process(ifcObjectPlacement);
        }

        static Transform ProcessPlacementBase(IFCAnyHandle placement)
        {
            IFCAnyHandle location = IFCAnyHandleUtil.GetInstanceAttribute(placement, "Location");
            return Transform.CreateTranslation(IFCPoint.ProcessScaledLengthIFCCartesianPoint(location));
        }

        static Transform ProcessAxis2Placement2D(IFCAnyHandle placement)
        {
            IFCAnyHandle refDirection = IFCAnyHandleUtil.GetInstanceAttribute(placement, "RefDirection");
            XYZ refDirectionX = 
                IFCAnyHandleUtil.IsNullOrHasNoValue(refDirection) ? XYZ.BasisX : IFCPoint.ProcessNormalizedIFCDirection(refDirection);
            XYZ refDirectionY = new XYZ(-refDirectionX.Y, refDirectionX.X, 0.0);

            Transform lcs = ProcessPlacementBase(placement);
            lcs.BasisX = refDirectionX;
            lcs.BasisY = refDirectionY;
            lcs.BasisZ = refDirectionX.CrossProduct(refDirectionY);

            return lcs;
        }

        static Transform ProcessAxis2Placement3D(IFCAnyHandle placement)
        {
            IFCAnyHandle axis = IFCAnyHandleUtil.GetInstanceAttribute(placement, "Axis");
            IFCAnyHandle refDirection = IFCAnyHandleUtil.GetInstanceAttribute(placement, "RefDirection");

            XYZ axisXYZ = IFCAnyHandleUtil.IsNullOrHasNoValue(axis) ?
                XYZ.BasisZ : IFCPoint.ProcessNormalizedIFCDirection(axis);
            XYZ refDirectionXYZ = IFCAnyHandleUtil.IsNullOrHasNoValue(refDirection) ?
                XYZ.BasisX : IFCPoint.ProcessNormalizedIFCDirection(refDirection);
            Transform lcs = ProcessPlacementBase(placement);

            XYZ lcsX = (refDirectionXYZ - refDirectionXYZ.DotProduct(axisXYZ) * axisXYZ).Normalize();
            XYZ lcsY = axisXYZ.CrossProduct(lcsX).Normalize();

            lcs.BasisX = lcsX;
            lcs.BasisY = lcsY;
            lcs.BasisZ = axisXYZ;
            return lcs;
        }

        /// <summary>
        /// Convert an IfcAxis1Placement into a transform.
        /// </summary>
        /// <param name="placement">The placement handle.</param>
        /// <returns>The transform.</returns>
        public static Transform ProcessIFCAxis1Placement(IFCAnyHandle ifcPlacement)
        {
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcPlacement))
                return Transform.Identity;

            Transform transform;
            if (IFCImportFile.TheFile.TransformMap.TryGetValue(ifcPlacement.StepId, out transform))
                return transform;

            if (!IFCAnyHandleUtil.IsSubTypeOf(ifcPlacement, IFCEntityType.IfcAxis1Placement))
            {
                IFCImportFile.TheLog.LogUnhandledSubTypeError(ifcPlacement, "IfcAxis1Placement", false);
                transform = Transform.Identity;
            }

            IFCAnyHandle ifcAxis = IFCAnyHandleUtil.GetInstanceAttribute(ifcPlacement, "Axis");
            XYZ norm = IFCAnyHandleUtil.IsNullOrHasNoValue(ifcAxis) ? XYZ.BasisZ : IFCPoint.ProcessNormalizedIFCDirection(ifcAxis);

            transform = ProcessPlacementBase(ifcPlacement);
            Plane arbitraryPlane = new Plane(norm, transform.Origin);

            transform.BasisX = arbitraryPlane.XVec;
            transform.BasisY = arbitraryPlane.YVec;
            transform.BasisZ = norm;
        
            IFCImportFile.TheFile.TransformMap[ifcPlacement.StepId] = transform;
            return transform;
        }
        
        /// <summary>
        /// Convert an IfcAxis2Placement into a transform.
        /// </summary>
        /// <param name="placement">The placement handle.</param>
        /// <returns>The transform.</returns>
        public static Transform ProcessIFCAxis2Placement(IFCAnyHandle ifcPlacement)
        {
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcPlacement))
                return Transform.Identity;

            Transform transform;
            if (IFCImportFile.TheFile.TransformMap.TryGetValue(ifcPlacement.StepId, out transform))
                return transform;

            if (IFCAnyHandleUtil.IsSubTypeOf(ifcPlacement, IFCEntityType.IfcAxis2Placement2D))
                transform = ProcessAxis2Placement2D(ifcPlacement);
            else if (IFCAnyHandleUtil.IsSubTypeOf(ifcPlacement, IFCEntityType.IfcAxis2Placement3D))
                transform = ProcessAxis2Placement3D(ifcPlacement);
            else
            {
                IFCImportFile.TheLog.LogUnhandledSubTypeError(ifcPlacement, "IfcAxis2Placement", false);
                transform = Transform.Identity;
            }

            IFCImportFile.TheFile.TransformMap[ifcPlacement.StepId] = transform;
            return transform;
        }

        override protected void Process(IFCAnyHandle objectPlacement)
        {
            base.Process(objectPlacement);

            IFCAnyHandle placementRelTo = IFCAnyHandleUtil.GetInstanceAttribute(objectPlacement, "PlacementRelTo");
            IFCAnyHandle relativePlacement = IFCAnyHandleUtil.GetInstanceAttribute(objectPlacement, "RelativePlacement");

            m_RelativeTo =
                IFCAnyHandleUtil.IsNullOrHasNoValue(placementRelTo) ? null : ProcessIFCObjectPlacement(placementRelTo);
            RelativeTransform = ProcessIFCAxis2Placement(relativePlacement);
        }

        /// <summary>
        /// Processes an IfcObjectPlacement object.
        /// </summary>
        /// <param name="objectPlacement">The IfcObjectPlacement handle.</param>
        /// <returns>The IFCLocation object.</returns>
        public static IFCLocation ProcessIFCObjectPlacement(IFCAnyHandle ifcObjectPlacement)
        {
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcObjectPlacement))
            {
                IFCImportFile.TheLog.LogNullError(IFCEntityType.IfcObjectPlacement);
                return null;
            }

            IFCEntity location;
            if (IFCImportFile.TheFile.EntityMap.TryGetValue(ifcObjectPlacement.StepId, out location))
                return (location as IFCLocation);

            if (IFCAnyHandleUtil.IsSubTypeOf(ifcObjectPlacement, IFCEntityType.IfcLocalPlacement))
                return new IFCLocation(ifcObjectPlacement);

            //LOG: ERROR: Not processed object placement.
            return new IFCLocation();
        }

        /// <summary>
        /// Removes the relative transform for a site.
        /// </summary>
        public static void RemoveRelativeTransformForSite(IFCSite site)
        {
            if (site == null || site.ObjectLocation == null || site.ObjectLocation.RelativeTransform == null)
                return;
            site.ObjectLocation.RelativeTransform = Transform.Identity;
        }
    }
}
