using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UH60FastRopeInsertion
{
    // Authored transaction adapter for the installed SPT4.1.6 movement state. Native Teleport
    // changes only current position/permission; relink fallback also reads old successful
    // samples, old sample height, path corners and the pending impostor motion frame.
    internal sealed class BotLandingCache
    {
        private sealed class Slot
        {
            internal readonly object Owner;
            internal readonly FieldInfo Field;
            internal Slot(object owner,string name,Type expected)
            {
                Owner=owner;
                for(Type t=owner.GetType();t!=null && Field==null;t=t.BaseType)
                    Field=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(Field==null || Field.IsInitOnly || (expected!=null && Field.FieldType!=expected)
                    || (expected==null && Field.FieldType.IsValueType))
                    throw new InvalidOperationException("Unsupported native landing field: "+owner.GetType().FullName+"."+name);
            }
            internal void Set(object value){Field.SetValue(Owner,value);}
        }
        private readonly Slot[] _position,_anchor,_zero,_time;
        private readonly Slot _freshSample,_retry,_oldOffsetTime,_linked,_allow,_used,_nextPath,_remain,_oldPath;
        private readonly object _avoidance,_inertia;
        private readonly MethodInfo _dropAvoidance,_dropInertia;
        private readonly Slot _successful;

        internal static BotLandingCache Bind(object mover)
        {
            object path=mover.GetType().GetProperty("ActualPathController")?.GetValue(mover,null);
            object context=mover.GetType().GetField("_lastContext")?.GetValue(mover);
            if(path==null)throw new InvalidOperationException("Native bot path controller not ready for landing.");
            return new BotLandingCache(mover,path,context);
        }

        internal BotLandingCache(object mover,object path,object context)
        {
            Slot[] Slots(object owner,Type type,params string[] names)
            {
                var result=new Slot[names.Length];for(int i=0;i<names.Length;i++)result[i]=new Slot(owner,names[i],type);return result;
            }
            var position=new List<Slot>(Slots(mover,typeof(Vector3),"_positionOnWay","_positionOnWayCasted",
                "_prevLinkPos","_lastPos","_prevFramePos","_currentTargetPoint"));
            position.AddRange(Slots(path,typeof(Vector3),"StopPos"));
            var anchor=new List<Slot>(Slots(mover,typeof(Vector3),"_prevSuccessLinkedFrom","_prevSuccsesLinkedFrom","_lastGoodCastPoint"));
            anchor.AddRange(Slots(path,typeof(Vector3),"_prevCorner","_currentCornerPoint","vector3_0"));
            var zero=new List<Slot>(Slots(mover,typeof(Vector3),"_prevOffsetGoodCasted","_dirCurPoint"));
            if(context!=null)
            {
                position.AddRange(Slots(context,typeof(Vector3),"PrevPosPlayer","PrevPosWay","TargetPos"));
                zero.AddRange(Slots(context,typeof(Vector3),"DirectionMove"));
                _used=new Slot(context,"Used",typeof(bool));_nextPath=new Slot(context,"NextTargetPath",null);
                _remain=new Slot(context,"RemainDist",typeof(float));
            }
            _position=position.ToArray();_anchor=anchor.ToArray();_zero=zero.ToArray();
            _time=Slots(mover,typeof(float),"_prevPosLinkedTime","_prevFrameTime","_lastTimePosChanged");
            _freshSample=new Slot(mover,"_lastGoodCastPointTime",typeof(float));
            _retry=new Slot(mover,"_next",typeof(float));
            _oldOffsetTime=new Slot(mover,"_prevOffsetGoodCastedTime",typeof(float));
            _linked=new Slot(mover,"_linkedToNavmeshInitially",typeof(bool));
            _allow=new Slot(mover,"_allowTeleport",typeof(bool));
            _oldPath=new Slot(path,"_lastPath",null);
            _successful=new Slot(mover,"_prevSuccessLinkedFrom",typeof(Vector3));
            _avoidance=mover.GetType().GetField("LocalAvoidance")?.GetValue(mover);
            _inertia=mover.GetType().GetField("_inertion")?.GetValue(mover);
            _dropAvoidance=_avoidance?.GetType().GetMethod("DropOffset",Type.EmptyTypes);
            _dropInertia=_inertia?.GetType().GetMethod("DropOffset",Type.EmptyTypes);
            if(_dropAvoidance==null || (_inertia!=null && _dropInertia==null))
                throw new InvalidOperationException("Unsupported native bot motion-offset reset.");
        }

        internal void Rebase(Vector3 position,Vector3 validatedAnchor,float now)
        {
            foreach(var slot in _position)slot.Set(position);
            foreach(var slot in _anchor)slot.Set(validatedAnchor);
            foreach(var slot in _zero)slot.Set(Vector3.zero);
            foreach(var slot in _time)slot.Set(now);
            _freshSample.Set(now);_retry.Set(now-1f);_oldOffsetTime.Set(now-2f);
            _linked.Set(true);_allow.Set(true);_oldPath.Set(null);
            _used?.Set(true);_nextPath?.Set(null);_remain?.Set(0f);
            _dropAvoidance.Invoke(_avoidance,null);_dropInertia?.Invoke(_inertia,null);
        }

        internal string Describe(){return "linkedAnchor="+_successful.Field.GetValue(_successful.Owner);}
    }
}
